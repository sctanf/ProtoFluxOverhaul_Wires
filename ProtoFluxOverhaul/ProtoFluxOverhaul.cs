using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using Renderite.Shared;
using ResoniteModLoader;

namespace ProtoFluxOverhaul;

public partial class ProtoFluxOverhaul : ResoniteMod {
	internal const string VERSION = "1.5.0";
	public override string Name => "ProtoFluxOverhaul_Wires";
	public override string Author => "Dexy, NepuShiro";
	public override string Version => VERSION;
	public override string Link => "https://github.com/sctanf/ProtoFluxOverhaul_Wires";

	// Configuration
	public static ModConfiguration Config;

	// ============ BASIC SETTINGS ============
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<dummy> SPACER_BASIC = new("spacerMain", "--- Main Settings ---", () => new dummy());
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> ENABLED = new("Enabled", "Should ProtoFluxOverhaul be Enabled?", () => true);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> AUTO_REBUILD_SELECTED_NODES = new("Auto Rebuild Selected Nodes", "When selecting ProtoFlux nodes, automatically rebuild them with ProtoFluxOverhaul styling (bypasses permission checks, not that this is only temporary and is reverted to the original behavior when the nodes are packed/unpacked).", () => false);

	// ============ ANIMATION SETTINGS ============
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<dummy> SPACER_ANIMATION = new("spacerAnimation", "--- Animation Settings ---", () => new dummy());
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<float2> SCROLL_SPEED = new("Scroll Speed", "Scroll Speed (X,Y)", () => new float2(0f, 0f));
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<float2> SCROLL_REPEAT = new("Scroll Repeat Interval", "Scroll Repeat Interval (X,Y)", () => new float2(1f, 1f));

	// ============ TEXTURE URLS ============
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<dummy> SPACER_TEXTURES = new("spacerTextures", "--- Texture URLs ---", () => new dummy());
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<Uri> WIRE_TEXTURE = new("Wire Texture", "Wire Texture URL", () => new Uri("resdb:///3b1b111048a828d92a0613fca0bfdee59c93f84428b5c27d1f9ce3bc86bf15c6.png"));

	// ============ ADVANCED TEXTURE SETTINGS ============
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<dummy> SPACER_ADVANCED = new("spacerAdvanced", "--- Advanced Texture Settings ---", () => new dummy());
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<int> ANISOTROPIC_LEVEL = new("Anisotropic Level", "Anisotropic Level", () => 16);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> MIPMAPS = new("Generate MipMaps", "Generate MipMaps", () => true);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> KEEP_ORIGINAL_MIPMAPS = new("Keep Original MipMaps", "Keep Original MipMaps", () => false);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<Filtering> MIPMAP_FILTER = new("MipMap Filter", "MipMap Filter", () => Filtering.Lanczos3);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> UNCOMPRESSED = new("Uncompressed Texture", "Uncompressed Texture", () => true);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> DIRECT_LOAD = new("Direct Load", "Direct Load", () => true);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> FORCE_EXACT_VARIANT = new("Force Exact Variant", "Force Exact Variant", () => true);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> CRUNCH_COMPRESSED = new("Crunch Compression", "Use Crunch Compression", () => false);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<TextureCompression> PREFERRED_FORMAT = new("Preferred Texture Format", "Preferred Texture Format", () => TextureCompression.BC3_Crunched);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> READABLE = new("Readable Texture", "Readable Texture", () => true);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<TextureFilterMode> FILTER_MODE = new("Texture Filter Mode", "Texture Filter Mode", () => TextureFilterMode.Anisotropic);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<TextureWrapMode> WRAP_MODE_U = new("Texture Wrap Mode U", "Texture Wrap Mode U", () => TextureWrapMode.Repeat);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<TextureWrapMode> WRAP_MODE_V = new("Texture Wrap Mode V", "Texture Wrap Mode V", () => TextureWrapMode.Clamp);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<ColorProfile> PREFERRED_PROFILE = new("Preferred Color Profile", "Preferred Color Profile", () => ColorProfile.sRGBAlpha);

	/// <summary>
	/// Shared ownership/host permission check for any component.
	/// Centralized to keep all patches consistent and reduce duplication.
	/// </summary>
	private static bool HasPermission(Component component) {
		try {
			if (component == null || component.Slot == null) {
				return false;
			}

			// Get the component's slot owner (allocation info)
			component.Slot.ReferenceID.ExtractIDs(out ulong slotPosition, out byte slotUser);
			User slotAllocUser = component.World.GetUserByAllocationID(slotUser);

			// If the slot allocation isn't valid, fall back to component allocation.
			if (slotAllocUser == null || slotPosition < slotAllocUser.AllocationIDStart) {
				component.ReferenceID.ExtractIDs(out ulong componentPosition, out byte componentUser);
				User componentAllocUser = component.World.GetUserByAllocationID(componentUser);

				bool hasPermission = (componentAllocUser != null &&
					componentPosition >= componentAllocUser.AllocationIDStart &&
					componentAllocUser == component.LocalUser);

				return hasPermission;
			}

			bool result = slotAllocUser == component.LocalUser;
			return result;
		} catch (Exception) {
			// If anything goes wrong, deny permission to be safe
			return false;
		}
	}

	public override void OnEngineInit() {
		Config = GetConfiguration();
		Config.Save(true);

		Harmony harmony = new("com.Dexy.ProtoFluxOverhaul");
		harmony.PatchAll();

		Config.OnThisConfigurationChanged += (k) => {
			if (k.Key != ENABLED) {
				Engine.Current.GlobalCoroutineManager.StartTask(async () => {
					await default(ToWorld);
					foreach (var kvp in _pannerCache.ToList()) {
						var panner = kvp.Value;
						if (panner == null || panner.IsRemoved) {
							// Clean up stale cache entries
							_pannerCache.Remove(kvp.Key);
							continue;
						}

						// Ensure the panner is properly initialized before setting properties
						try {
							panner.Speed = Config.GetValue(SCROLL_SPEED);
							panner.Repeat = Config.GetValue(SCROLL_REPEAT);
						} catch (System.NullReferenceException) {
							// Skip this panner if it's not properly initialized
							continue;
						}

						// Panner/material/texture live on the PFO child slot
						var pfoSlot = kvp.Key;
						var fresnelMaterial = pfoSlot.GetComponent<FresnelMaterial>();
						if (fresnelMaterial != null) {
							try {
								var farTexture = GetOrCreateSharedTexture(pfoSlot, Config.GetValue(WIRE_TEXTURE));
								fresnelMaterial.FarTexture.Target = farTexture;

								var nearTexture = GetOrCreateSharedTexture(pfoSlot, Config.GetValue(WIRE_TEXTURE));
								fresnelMaterial.NearTexture.Target = nearTexture;
							} catch (Exception) {
							}
						}
					}
				});
			}
		};
	}
}
