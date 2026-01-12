using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Elements.Core;

using FrooxEngine;

namespace ProtoFluxOverhaul;

public partial class ProtoFluxOverhaul
{
	// Internal organization slot name for all per-wire mod components
	private const string PfoWireSlotName = "PFO_Wires";

	private static readonly Dictionary<bool, Panner2D> _pannerCache = new(); // two panners; forward and reverse
	private static readonly Dictionary<bool, FresnelMaterial> _materialCache = new();
	private static readonly Dictionary<bool, ValueDriver<float2>> _driverCache = new();
	private static World currentWorld = null; // i am very good at this
	private static readonly ConditionalWeakTable<MeshRenderer, object> _rendererCache = new();

	/// <summary>
	/// Gets or creates the child slot for all ProtoFluxOverhaul components on a wire.
	/// </summary>
	private static Slot GetOrCreatePfoSlot(Slot slot)
	{
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
	private static StaticTexture2D GetOrCreateSharedTexture(Slot slot, Uri uri)
	{
		ArgumentNullException.ThrowIfNull(slot);

		StaticTexture2D texture = slot.GetComponentOrAttach<StaticTexture2D>();
		texture.URL.Value = uri;

		return texture;
	}
}
