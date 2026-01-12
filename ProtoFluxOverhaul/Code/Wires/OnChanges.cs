using System;

using Elements.Core;

using FrooxEngine;
using FrooxEngine.ProtoFlux;

using HarmonyLib;

namespace ProtoFluxOverhaul;

public partial class ProtoFluxOverhaul
{
	[HarmonyPatch(typeof(ProtoFluxWireManager), "OnChanges")]
	private class ProtoFluxWireManager_OnChanges_Patch
	{
		private static bool TryIsOutputWire(ProtoFluxWireManager wire, StripeWireMesh wireMesh, out bool isOutput)
		{
			isOutput = false;
			if (wire == null) return false;

			// Prefer the explicit wire type if it is correctly set.
			if (wire.Type.Value == WireType.Output) { isOutput = true; return true; }
			if (wire.Type.Value == WireType.Input) { isOutput = false; return true; }

			// Fallback: infer from mesh tangent direction (engine Setup uses +/-X * TANGENT_MAGNITUDE).
			if (wireMesh != null)
			{
				try
				{
					isOutput = wireMesh.Tangent0.Value.x > 0f;
					return true;
				}
				catch { }
			}
			return false;
		}
		private static void SetupMaterial(Slot pfoSlot, Slot matSlot, FresnelMaterial originalMaterial, bool direction) {
			// Create a new material on the PFO slot
			var newMaterial = matSlot.AttachComponent<FresnelMaterial>();
			newMaterial.NearColor.Value = colorX.White;
			newMaterial.FarColor.Value = colorX.White;
			newMaterial.Sidedness.Value = originalMaterial.Sidedness.Value;
			newMaterial.UseVertexColors.Value = originalMaterial.UseVertexColors.Value;
			newMaterial.BlendMode.Value = originalMaterial.BlendMode.Value;
			// newMaterial.ZWrite.Value = originalMaterial.ZWrite.Value;
			newMaterial.ZWrite.Value = ZWrite.Off; // may cause z errors but renders wires behind transparency
			newMaterial.NearTextureScale.Value = originalMaterial.NearTextureScale.Value;
			newMaterial.NearTextureOffset.Value = originalMaterial.NearTextureOffset.Value;
			newMaterial.FarTextureScale.Value = originalMaterial.FarTextureScale.Value;
			newMaterial.FarTextureOffset.Value = originalMaterial.FarTextureOffset.Value;
			newMaterial.PolarPower.Value = direction ? 1f : 0f; // flag direction
			_materialCache[direction] = newMaterial;

			if (!_pannerCache.TryGetValue(direction, out var panner) || panner.IsRemoved)
				_pannerCache[direction] = pfoSlot.AttachComponent<Panner2D>();
			_pannerCache[direction].Target = newMaterial.FarTextureOffset;

			if (!_driverCache.TryGetValue(direction, out var driver) || driver.IsRemoved)
				_driverCache[direction] = pfoSlot.AttachComponent<ValueDriver<float2>>();

			_driverCache[direction].DriveTarget.Target = newMaterial.NearTextureOffset;
			_driverCache[direction].ValueSource.Target = newMaterial.FarTextureOffset;
		}
		public static void Postfix(ProtoFluxWireManager __instance, SyncRef<MeshRenderer> ____renderer, SyncRef<StripeWireMesh> ____wireMesh)
		{
			try
			{
				// Skip if mod is disabled or required components are missing
				if (!Config.GetValue(ENABLED) ||
					__instance == null ||
					!__instance.Enabled ||
					____renderer?.Target == null ||
					____wireMesh?.Target == null ||
					__instance.Slot == null) return;

				// === User Permission Check ===
				if (!HasPermission(__instance))
				{
					// Skip silently for unauthorized wires to reduce log spam
					return;
				}

				// Get or create the PFO child slot for all additional mod components
				var pfoSlot = GetOrCreatePfoSlot(__instance.Slot);
				if (pfoSlot == null) return;

				var matSlot = GetOrCreatePfoMatSlot(pfoSlot);
				if (matSlot == null) return;

				// TODO: yea im just cooked. delete all the caches if the user switches worlds, itll just get regenerated anyway
				if (currentWorld != __instance.World) {
					currentWorld = __instance.World;
					_pannerCache.Clear();
					_materialCache.Clear();
					_driverCache.Clear();
				}

				// === Material Setup ===
				var renderer = ____renderer?.Target;
				if (renderer == null) return;

				// need to get direction to find what set of components needed and what material to assign
				TryIsOutputWire(__instance, ____wireMesh.Target, out bool isOutputDir);

				// checking if the renderer is already setup
				if (!_rendererCache.Contains(renderer) || renderer.Material.Target == null || renderer.Material.Target.IsRemoved)
				{
					// what does this do? the original check does not make sense to me either
					if (renderer.Material.Target is not FresnelMaterial originalMaterial) {
						return;
					}

					// check if the renderer is driven by something else (because we cant write to that)
					if (renderer.Material.IsDriven)
					{
						// TODO: does its material exist? maybe try to break the drive otherwise
						UniLog.Log("Ignore already driven material");
						// add to cache to ignore later
						if (!_rendererCache.Contains(renderer))
							_rendererCache.AddUnique(renderer);
						return;
					}

					// make sure everything exists
					if (_materialCache.Count != 2 || _materialCache[true].IsRemoved || _materialCache[false].IsRemoved) {
						// first assign existing stuff (if it exists)
						var existingMats = matSlot.GetComponents<FresnelMaterial>();
						var existingPanners = pfoSlot.GetComponents<Panner2D>();
						// just explode if there are too many
						if (existingMats.Count > 2 || existingPanners.Count > 2) {
							UniLog.Warning("Unexpected components found!");
							return;
						}

						foreach (var mat in existingMats) {
							if (mat.IsRemoved)
								continue;
							// kinda stupid. use PolarPower field to tell things apart
							bool direction = mat.PolarPower == 1f;
							_materialCache[direction] = mat;
							foreach (var pan in existingPanners) {
								if (pan.IsRemoved)
									continue;
								// associate panner
								if (pan.Target == mat.FarTextureOffset) {
									_pannerCache[direction] = pan;
								}
							}
						}

						// now create anything missing..
						// TODO: everything will explode horribly if other components are missing for some reason but its probably not gonna happen
						if (!_materialCache.TryGetValue(true, out FresnelMaterial value) || value.IsRemoved) {
							SetupMaterial(pfoSlot, matSlot, originalMaterial, true);
						}
						if (!_materialCache.TryGetValue(false, out FresnelMaterial value1) || value1.IsRemoved) {
							SetupMaterial(pfoSlot, matSlot, originalMaterial, false);
						}
					}

					// set the material without drive
					// drive is not really needed, if the material explodes (which the reference would have pointed to) then nothing used it anyway
					renderer.Material.Target = _materialCache[isOutputDir];

					// finally put the renderer in cache to ignore next time
					if (!_rendererCache.Contains(renderer))
						_rendererCache.AddUnique(renderer);
				}

				// now actually assign things
				// output dir determines set of components to use
				var panner = _pannerCache[isOutputDir];

				panner.Repeat = Config.GetValue(SCROLL_REPEAT);

				var baseSpeed = Config.GetValue(SCROLL_SPEED);
				bool flipDirection = !isOutputDir; // inputs flip
				float directionFactor = flipDirection ? -1f : 1f;
				panner.Speed = new float2(baseSpeed.x * directionFactor, baseSpeed.y);

				var texture = GetOrCreateSharedTexture(matSlot, Config.GetValue(WIRE_TEXTURE));
				_materialCache[isOutputDir].FarTexture.Target = texture;
				_materialCache[isOutputDir].NearTexture.Target = texture;
			}
			catch (Exception e) {
				UniLog.Error(e.Message);
			}
		}
	}
}

