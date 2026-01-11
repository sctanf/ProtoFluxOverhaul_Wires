using System;

using Elements.Core;

using FrooxEngine;
using FrooxEngine.ProtoFlux;

using HarmonyLib;

using ProtoFlux.Core;
using ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Slots;

using Renderite.Shared;

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
			_matsCache[direction] = newMaterial;

			if (!_referenceCache.TryGetValue(direction, out var reference) || reference.IsRemoved)
				_referenceCache[direction] = pfoSlot.AttachComponent<ReferenceField<IAssetProvider<Material>>>();
			_referenceCache[direction].Reference.Target = newMaterial;

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

				// TODO: yea im just cooked. delete all the caches if the user switches worlds. aaaah. why did i not think this through. itll just get regenerated anyway so nbd ig
				if (currentWorld != __instance.World) {
					currentWorld = __instance.World;
					_pannerCache.Clear();
					_referenceCache.Clear();
					_matsCache.Clear();
					_driverCache.Clear();
				}

				// === Material Setup ===
				var renderer = ____renderer?.Target;
				if (renderer == null) return;

				// need to get direction to find what set of components needed and what material to assign
				TryIsOutputWire(__instance, ____wireMesh.Target, out bool isOutputDir);

				// checking if the renderer is already setup
				if (!_materialCache.TryGetValue(renderer, out var fresnelMaterial) || fresnelMaterial == null || fresnelMaterial.IsRemoved)
				{
					// what does this do? the original check does not make sense to me either :P
					if (renderer.Material.Target is not FresnelMaterial originalMaterial) {
						return;
					}

					// now check if the renderer is already driven
					if (renderer.Material.IsDriven)
					{
						UniLog.Log("Ignore already driven material");
						// add to cache to ignore later
						_materialCache[renderer] = (FresnelMaterial)renderer.Material.Target; // TODO: im dumb. is this gonna crash
						return;
					}

					// make sure everything exists
					if (_matsCache.Count != 2 || _matsCache[true].IsRemoved || _matsCache[false].IsRemoved) {
						// first assign existing stuff (if it exists)
						var existingMats = matSlot.GetComponents<FresnelMaterial>();
						var existingPanners = pfoSlot.GetComponents<Panner2D>();
						var matSources = pfoSlot.GetComponents<ReferenceField<IAssetProvider<Material>>>();
						// just explode if there are too many lol
						if (existingMats.Count > 2 || existingPanners.Count > 2 || matSources.Count > 2) {
							UniLog.Warning("Unexpected components found!");
							return;
						}

						foreach (var mat in existingMats) {
							if (mat.IsRemoved)
								continue;
							// kinda stupid. use PolarPower field to tell things apart lol
							bool direction = mat.PolarPower == 1f;
							_matsCache[direction] = mat;
							foreach (var pan in existingPanners) {
								if (pan.IsRemoved)
									continue;
								// associate panner
								if (pan.Target == mat.FarTextureOffset) {
									_pannerCache[direction] = pan;
								}
							}
							foreach (var src in matSources) {
								if (src.IsRemoved)
									continue;
								// associate source
								if (src.Reference.Target == mat) {
									_referenceCache[direction] = src;
								}
							}
						}

						// now create anything missing..
						// TODO: everything will explode horribly if other components are missing for some reason but idc tbh, its probably not gonna happen lol
						if (!_matsCache.TryGetValue(true, out FresnelMaterial value) || value.IsRemoved) {
							SetupMaterial(pfoSlot, matSlot, originalMaterial, true);
						}
						if (!_matsCache.TryGetValue(false, out FresnelMaterial value1) || value1.IsRemoved) {
							SetupMaterial(pfoSlot, matSlot, originalMaterial, false);
						}
					}

					var materialSource = _referenceCache[isOutputDir];

					// keep drive on the wire slot itself, we dont really care what happens to it tbh
					var materialCopy = __instance.Slot.GetComponentOrAttach<ReferenceCopy<IAssetProvider<Material>>>();
					materialCopy.Source.Target = materialSource.Reference;
					materialCopy.Target.Target = renderer.Material;
					materialCopy.WriteBack.Value = false;

					// finally set the material in cache
					_materialCache[renderer] = _matsCache[isOutputDir];
				}

				// === Wire Mesh Setup ===
				var stripeMesh = ____wireMesh?.Target;
				if (stripeMesh != null) {
					stripeMesh.Profile.Value = ColorProfile.sRGB;
				}

				// now actually assign things
				// output dir determines set of components to use
				fresnelMaterial = _matsCache[isOutputDir];
				var panner = _pannerCache[isOutputDir];

				panner.Repeat = Config.GetValue(SCROLL_REPEAT);

				var baseSpeed = Config.GetValue(SCROLL_SPEED);
				// The engine wire Type isn't always reliable in modded contexts; infer direction from mesh if needed.
				bool flipDirection = !isOutputDir; // inputs flip
				float directionFactor = flipDirection ? -1f : 1f;
				panner.Speed = new float2(baseSpeed.x * directionFactor, baseSpeed.y);

				var farTexture = GetOrCreateSharedTexture(matSlot, Config.GetValue(WIRE_TEXTURE));
				fresnelMaterial.FarTexture.Target = farTexture;

				var nearTexture = GetOrCreateSharedTexture(matSlot, Config.GetValue(WIRE_TEXTURE));
				fresnelMaterial.NearTexture.Target = nearTexture;
			}
			catch (Exception e) {
				UniLog.Error(e.Message);
			}
		}
	}
}

