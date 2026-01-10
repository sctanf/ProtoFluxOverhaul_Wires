using System;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using HarmonyLib;
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

				// === Material Setup ===
				var renderer = ____renderer?.Target;
				if (renderer == null) return;

				if (!_materialCache.TryGetValue(renderer, out var fresnelMaterial) || fresnelMaterial == null || fresnelMaterial.IsRemoved)
				{
					if (renderer.Material.Target is not FresnelMaterial originalMaterial) {
						return;
					}

					// Check if a custom material already exists on the PFO slot (for multiplayer safety)
					var existingMaterial = pfoSlot.GetComponent<FresnelMaterial>();
					if (existingMaterial != null)
					{
						// Use the existing custom material instead of creating a new one
						_materialCache[renderer] = existingMaterial;
						fresnelMaterial = existingMaterial;
					}
					else
					{
						// Create a new material on the PFO slot
						var newMaterial = pfoSlot.AttachComponent<FresnelMaterial>();
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

						_materialCache[renderer] = newMaterial;
						fresnelMaterial = newMaterial;
					}

					// Use ReferenceCopy to drive the renderer's material from our custom material
					// This creates a dynamic link instead of direct assignment
					if (!renderer.Material.IsDriven)
					{
						// Create ReferenceField and ReferenceCopy on the PFO slot
						var materialSource = pfoSlot.GetComponentOrAttach<ReferenceField<IAssetProvider<Material>>>();
						materialSource.Reference.Target = fresnelMaterial;

						// Create ReferenceCopy to drive the renderer's material
						var materialCopy = pfoSlot.GetComponentOrAttach<ReferenceCopy<IAssetProvider<Material>>>();
						materialCopy.Source.Target = materialSource.Reference;
						materialCopy.Target.Target = renderer.Material;
						materialCopy.WriteBack.Value = false;
					}
				}

				// === Wire Mesh Setup ===
				var stripeMesh = ____wireMesh?.Target;
				if (stripeMesh != null)
				{
					stripeMesh.Profile.Value = ColorProfile.sRGB;
				}

				// === Animation Setup ===
				// Get or create Panner2D for scrolling effect on the PFO slot
				if (!_pannerCache.TryGetValue(pfoSlot, out var panner))
				{
					panner = pfoSlot.GetComponentOrAttach<Panner2D>();
					_pannerCache[pfoSlot] = panner;
				}

				try
				{
					panner.Speed = Config.GetValue(SCROLL_SPEED);
					panner.Repeat = Config.GetValue(SCROLL_REPEAT);
				}
				catch (NullReferenceException)
				{
					return;
				}

				var baseSpeed = Config.GetValue(SCROLL_SPEED);
				// The engine wire Type isn't always reliable in modded contexts; infer direction from mesh if needed.
				bool isOutputDir = false;
				TryIsOutputWire(__instance, ____wireMesh.Target, out isOutputDir);
				bool flipDirection = !isOutputDir; // inputs flip
				float directionFactor = flipDirection ? -1f : 1f;
				panner.Speed = new float2(baseSpeed.x * directionFactor, baseSpeed.y);

				var farTexture = GetOrCreateSharedTexture(pfoSlot, Config.GetValue(WIRE_TEXTURE));
				fresnelMaterial.FarTexture.Target = farTexture;

				var nearTexture = GetOrCreateSharedTexture(pfoSlot, Config.GetValue(WIRE_TEXTURE));
				fresnelMaterial.NearTexture.Target = nearTexture;

				// === Texture Offset Setup ===
				// Setup texture offset drivers if they don't exist
				if (!fresnelMaterial.FarTextureOffset.IsLinked && panner.Target == null)
				{
					panner.Target = fresnelMaterial.FarTextureOffset;
				}

				if (!fresnelMaterial.NearTextureOffset.IsLinked)
				{
					// Create a ValueDriver on the PFO slot to link NearTextureOffset to the same panner output
					ValueDriver<float2> newNearDrive = pfoSlot.GetComponentOrAttach<ValueDriver<float2>>();

					if (!newNearDrive.DriveTarget.IsLinkValid && panner.Target != null)
					{
						newNearDrive.DriveTarget.Target = fresnelMaterial.NearTextureOffset;
						newNearDrive.ValueSource.Target = panner.Target;
					}
				}
			}
			catch (Exception) {
			}
		}
	}
}

