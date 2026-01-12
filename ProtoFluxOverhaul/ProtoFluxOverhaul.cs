using System;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
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

	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<bool> ENABLED = new("Enabled", "Should ProtoFluxOverhaul be Enabled?", () => true);
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<float2> SCROLL_SPEED = new("Scroll Speed", "Scroll Speed (X,Y)", () => new float2(0f, 0f));
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<float2> SCROLL_REPEAT = new("Scroll Repeat Interval", "Scroll Repeat Interval (X,Y)", () => new float2(1f, 1f));
	[AutoRegisterConfigKey] public static readonly ModConfigurationKey<Uri> WIRE_TEXTURE = new("Wire Texture", "Wire Texture URL", () => new Uri("resdb:///3b1b111048a828d92a0613fca0bfdee59c93f84428b5c27d1f9ce3bc86bf15c6.png"));

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
	}
}
