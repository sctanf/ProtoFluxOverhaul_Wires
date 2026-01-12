using System;

using Elements.Core;

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
		public static void OnDestroy_Postfix(ProtoFluxWireManager __instance, SyncRef<MeshRenderer> ____renderer) {
			// syncref/meshrenderer is already destroyed..
			static bool predicate(MeshRenderer p) { return p == null || p.IsRemoved; }
			_rendererCache.RemoveAll(predicate);
		}
	}
}

