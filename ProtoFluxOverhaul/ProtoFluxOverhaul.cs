using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Elements.Core;

using FrooxEngine;
using FrooxEngine.ProtoFlux;

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

	// Internal organization slot name for all per-wire mod components
	private const string PfoWireSlotName = "PFO_Wires";

	private static readonly Dictionary<bool, Panner2D> _pannerCache = new(); // two panners; forward and reverse
	private static readonly Dictionary<bool, FresnelMaterial> _materialCache = new();
	private static readonly Dictionary<bool, ValueDriver<float2>> _driverCache = new();
	private static World currentWorld = null; // i am very good at this
	private static readonly ConditionalWeakTable<MeshRenderer, object> _rendererCache = new();

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
		} catch {
			// If anything goes wrong, deny permission to be safe
			return false;
		}
	}
	public override void OnEngineInit() {
		Config = GetConfiguration();
		Config.Save(true);

		Harmony harmony = new("com.Dexy.ProtoFluxOverhaul");
		harmony.PatchAll();
		Msg("\U0001f408"); // mow

		Config.OnThisConfigurationChanged += (k) => {
			if (k.Key != ENABLED) {
				// will probably fail if you are not focused in the last world you created a wire
				currentWorld?.RunSynchronously(() => {
					foreach (var kvp in _pannerCache) {
						var panner = kvp.Value;
						if (panner == null || panner.IsRemoved) continue;

						panner.Repeat = Config.GetValue(SCROLL_REPEAT);
						var baseSpeed = Config.GetValue(SCROLL_SPEED);
						float directionFactor = kvp.Key ? 1f : -1f;
						panner.Speed = new float2(baseSpeed.x * directionFactor, baseSpeed.y);
					}
					Slot root = currentWorld.RootSlot;
					if (root != null && !root.IsRemoved) {
						Slot matSlot = GetOrCreatePfoMatSlot(currentWorld.RootSlot);
						if (matSlot != null) {
							GetOrCreateSharedTexture(matSlot, Config.GetValue(WIRE_TEXTURE));
						}
					}
				});
			}
		};
	}

	[HarmonyPatch(typeof(ProtoFluxWireManager), "OnChanges")]
	private class ProtoFluxWireManager_OnChanges_Patch {
		public static void Postfix(ProtoFluxWireManager __instance, SyncRef<MeshRenderer> ____renderer, SyncRef<StripeWireMesh> ____wireMesh) {
			// Skip if mod is disabled or required components are missing
			if (!Config.GetValue(ENABLED) ||
				__instance == null ||
				!__instance.Enabled ||
				____renderer?.Target == null ||
				____wireMesh?.Target == null ||
				__instance.Slot == null) return;

			if (!HasPermission(__instance)) {
				return;
			}

			var renderer = ____renderer?.Target;
			if (renderer == null) return;

			// checking if the renderer is already setup
			if (_rendererCache.TryGetValue(renderer, out object o) && renderer.Material.Target != null && !renderer.Material.Target.IsRemoved) {
				return;
			}

			// check if the renderer is driven by something else (because we cant write to that)
			if (renderer.Material.IsDriven) {
				// TODO: does its material exist? maybe try to break the drive otherwise
				Msg("Ignore already driven material");
				// add to cache to ignore later
				_rendererCache.TryAdd(renderer, null);
				return;
			}

			// TODO: yea im just cooked. delete all the caches if the user switches worlds, itll just get regenerated anyway
			if (currentWorld != __instance.World) {
				currentWorld = __instance.World;
				_pannerCache.Clear();
				_materialCache.Clear();
				_driverCache.Clear();
			}

			// make sure everything exists
			if (_materialCache.Count != 2 || _materialCache[true].IsRemoved || _materialCache[false].IsRemoved) {
				SetupInstance(__instance.Slot);
			}

			// need to get direction to find what material to assign
			TryIsOutputWire(__instance, ____wireMesh.Target, out bool isOutputDir);

			// set the material without drive
			// drive is not really needed, if the material explodes (which the reference would have pointed to) then nothing used it anyway
			renderer.Material.Target = _materialCache[isOutputDir];

			// finally put the renderer in cache to ignore next time
			_rendererCache.TryAdd(renderer, null);
		}
	}

	private static bool TryIsOutputWire(ProtoFluxWireManager wire, StripeWireMesh wireMesh, out bool isOutput) {
		isOutput = false;
		if (wire == null) return false;

		// Prefer the explicit wire type if it is correctly set.
		if (wire.Type.Value == WireType.Output) { isOutput = true; return true; }
		if (wire.Type.Value == WireType.Input) { isOutput = false; return true; }
		// there is also Reference wire type, what is it used for?

		// Fallback: infer from mesh tangent direction (engine Setup uses +/-X * TANGENT_MAGNITUDE).
		if (wireMesh != null) {
			try {
				isOutput = wireMesh.Tangent0.Value.x > 0f;
				return true;
			} catch { }
		}
		return false;
	}

	// try to find existing components, otherwise create them
	private static void SetupInstance(Slot slot) {
		// only need these slots during setup
		var pfoSlot = GetOrCreatePfoSlot(slot);
		if (pfoSlot == null) return;
		var matSlot = GetOrCreatePfoMatSlot(pfoSlot);
		if (matSlot == null) return;

		// first assign existing stuff (if it exists)
		var existingMats = matSlot.GetComponents<FresnelMaterial>();
		var existingPanners = pfoSlot.GetComponents<Panner2D>();
		// just explode if there are too many
		if (existingMats.Count > 2 || existingPanners.Count > 2) {
			Warn("Unexpected components found!");
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
			SetupMaterial(pfoSlot, matSlot, true);
		}
		if (!_materialCache.TryGetValue(false, out FresnelMaterial value1) || value1.IsRemoved) {
			SetupMaterial(pfoSlot, matSlot, false);
		}
	}

	// attach and setup custom material and respective panner
	private static void SetupMaterial(Slot pfoSlot, Slot matSlot, bool direction) {
		_materialCache[direction] = matSlot.AttachComponent<FresnelMaterial>();
		// from ProtoFluxWireManager.OnAttach();
		_materialCache[direction].NearColor.Value = new colorX(0.8f);
		_materialCache[direction].FarColor.Value = new colorX(1.4f);
		_materialCache[direction].Sidedness.Value = Sidedness.Double;
		_materialCache[direction].UseVertexColors.Value = true;
		_materialCache[direction].BlendMode.Value = BlendMode.Alpha;
		_materialCache[direction].ZWrite.Value = ZWrite.Off; // may cause z errors but renders wires behind transparency
		_materialCache[direction].PolarPower.Value = direction ? 1f : 0f; // flag direction

		if (!_pannerCache.TryGetValue(direction, out var panner) || panner.IsRemoved)
			_pannerCache[direction] = pfoSlot.AttachComponent<Panner2D>();
		_pannerCache[direction].Target = _materialCache[direction].FarTextureOffset;

		if (!_driverCache.TryGetValue(direction, out var driver) || driver.IsRemoved)
			_driverCache[direction] = pfoSlot.AttachComponent<ValueDriver<float2>>();

		_driverCache[direction].DriveTarget.Target = _materialCache[direction].NearTextureOffset;
		_driverCache[direction].ValueSource.Target = _materialCache[direction].FarTextureOffset;

		// now actually assign things
		_pannerCache[direction].Repeat = Config.GetValue(SCROLL_REPEAT);
		var baseSpeed = Config.GetValue(SCROLL_SPEED);
		float directionFactor = direction ? 1f : -1f;
		_pannerCache[direction].Speed = new float2(baseSpeed.x * directionFactor, baseSpeed.y);

		var texture = GetOrCreateSharedTexture(matSlot, Config.GetValue(WIRE_TEXTURE));
		_materialCache[direction].FarTexture.Target = texture;
		_materialCache[direction].NearTexture.Target = texture;
	}

	/// <summary>
	/// Gets or creates the child slot for all ProtoFluxOverhaul components on a wire.
	/// </summary>
	private static Slot GetOrCreatePfoSlot(Slot slot) {
		if (slot == null) return null;
		return slot.World.AssetsSlot.FindChildOrAdd(PfoWireSlotName + "_" + slot.LocalUser.UserID);
	}

	// these slots will get cleaned up automatically once the materials are unused
	private static Slot GetOrCreatePfoMatSlot(Slot slot) {
		if (slot == null) return null;
		Slot pfoSlot = GetOrCreatePfoSlot(slot);
		Slot matSlot = pfoSlot.FindChildOrAdd("Materials");
		if (matSlot == null) return null;
		// also setup cleanup now that this slot will exist
		pfoSlot.GetComponentOrAttach<DestroyWithoutChildren>();
		return matSlot;
	}

	/// <summary>
	/// Creates or retrieves a texture with specified settings directly on the wire slot.
	/// </summary>
	private static StaticTexture2D GetOrCreateSharedTexture(Slot slot, Uri uri) {
		ArgumentNullException.ThrowIfNull(slot);

		var texture = slot.GetComponentOrAttach<StaticTexture2D>();
		texture.URL.Value = uri;

		return texture;
	}
}
