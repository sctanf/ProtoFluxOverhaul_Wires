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
				// removing deleted renderers since they no longer need to be checked
				var renderer = ____renderer?.Target;
				if (renderer != null)
				{
					_materialCache.Remove(renderer);
				}
			}
			catch (Exception) {
			}
		}
	}
}

