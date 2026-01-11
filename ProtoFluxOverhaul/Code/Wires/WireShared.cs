using System;
using System.Collections.Generic;

using Elements.Core;

using FrooxEngine;

namespace ProtoFluxOverhaul;

public partial class ProtoFluxOverhaul
{
	// Internal organization slot name for all per-wire mod components
	private const string PfoWireSlotName = "PFO_Wires";

	private static readonly Dictionary<bool, Panner2D> _pannerCache = new(); // two panners; forward and reverse
	private static readonly Dictionary<bool, ReferenceField<IAssetProvider<Material>>> _referenceCache = new(); // and the two references to be copied from
	private static readonly Dictionary<bool, FresnelMaterial> _matsCache = new();
	private static readonly Dictionary<bool, ValueDriver<float2>> _driverCache = new();
	private static World currentWorld = null; // i am very good at this
	private static readonly Dictionary<MeshRenderer, FresnelMaterial> _materialCache = new();

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
	/// Finds the child slot for all ProtoFluxOverhaul components on a wire (does not create it).
	/// </summary>
	private static Slot FindPfoSlot(Slot slot) {
		if (slot == null) return null;
		return slot.World.AssetsSlot.FindChild(PfoWireSlotName + "_" + slot.LocalUser.UserID);
	}

	/// <summary>
	/// Creates or retrieves a texture with specified settings directly on the wire slot.
	/// </summary>
	private static StaticTexture2D GetOrCreateSharedTexture(Slot slot, Uri uri)
	{
		ArgumentNullException.ThrowIfNull(slot);

		StaticTexture2D texture = slot.GetComponentOrAttach<StaticTexture2D>();
		texture.URL.Value = uri;

		texture.FilterMode.Value = Config.GetValue(FILTER_MODE);
		texture.MipMaps.Value = Config.GetValue(MIPMAPS);
		texture.Uncompressed.Value = Config.GetValue(UNCOMPRESSED);
		texture.CrunchCompressed.Value = Config.GetValue(CRUNCH_COMPRESSED);
		texture.DirectLoad.Value = Config.GetValue(DIRECT_LOAD);
		texture.ForceExactVariant.Value = Config.GetValue(FORCE_EXACT_VARIANT);
		texture.AnisotropicLevel.Value = Config.GetValue(ANISOTROPIC_LEVEL);
		texture.WrapModeU.Value = Config.GetValue(WRAP_MODE_U);
		texture.WrapModeV.Value = Config.GetValue(WRAP_MODE_V);
		texture.KeepOriginalMipMaps.Value = Config.GetValue(KEEP_ORIGINAL_MIPMAPS);
		texture.MipMapFilter.Value = Config.GetValue(MIPMAP_FILTER);
		texture.Readable.Value = Config.GetValue(READABLE);
		texture.PowerOfTwoAlignThreshold.Value = 0.05f;

		return texture;
	}
}

