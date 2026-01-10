using System;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using HarmonyLib;

namespace ProtoFluxOverhaul;

public partial class ProtoFluxOverhaul
{
	// Patches for wire-related events
	[HarmonyPatch(typeof(ProtoFluxWireManager))]
	public class ProtoFluxWireManager_Patches
	{
		[HarmonyPatch("OnDestroy")]
		[HarmonyPostfix]
		public static void OnDestroy_Postfix(ProtoFluxWireManager __instance, SyncRef<MeshRenderer> ____renderer)
		{
			try
			{
				// === Cache cleanup (renderer->material) ===
				var renderer = ____renderer?.Target;
				if (renderer != null)
				{
					_materialCache.Remove(renderer);
				}

				// === Per-wire PFO slot cleanup ===
				// All mod components are attached under a dedicated PFO child slot on the wire slot.
				//
				// IMPORTANT: ProtoFlux Pack/Unpack can tear down and recreate wire managers transiently.
				// Destroying the PFO slot in those cases makes it look like "PFO_WireOverhaul was cleared out".
				// Only destroy the PFO slot for actual wire deletion / slot removal.
				var pfoSlot = FindPfoSlot(__instance?.Slot);
				if (pfoSlot != null)
				{
					_pannerCache.Remove(pfoSlot);

					bool isRealDelete = false;
					try
					{
						isRealDelete = __instance != null && __instance.DeleteHighlight != null && __instance.DeleteHighlight.Value;
					}
					catch
					{
						// Ignore - component may be partially torn down.
					}

					bool wireSlotRemoved = __instance?.Slot == null || __instance.Slot.IsRemoved;

					if (isRealDelete || wireSlotRemoved)
					{
						if (!pfoSlot.IsRemoved)
							pfoSlot.Destroy();
					}
				}
			}
			catch (Exception) {
			}
		}
	}
}

