using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Core;
using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    // Diagnostic backend only: an inactive world anchor and a manually projected IMGUI marker.
    // No player parenting, colliders, scene renderers, gameplay patches or network objects.
    internal sealed class PlaceholderHud : IHudRuntimePresentation
    {
        GameObject anchor;
        Texture2D pixel;
        int step;
        bool queued;
        HudPlacement placement;
        ContextStamp context;
        LocalViewBinding binding;
        readonly Guid instance = Guid.NewGuid();
        long draws, textureRejections, anchorsCreated, texturesCreated, destroyRequests;
        HudPlacement lastDraw;
        public HudPresentationDiagnostics Diagnostics => new(instance, draws, textureRejections, anchorsCreated, texturesCreated,
            destroyRequests, (anchor != null ? 1 : 0) + (pixel != null ? 1 : 0), lastDraw);
        public int InitializationStep => step;
        public bool HasResources => step != 0 || !ReferenceEquals(anchor, null) || !ReferenceEquals(pixel, null);
        public bool IsHealthy => (step < 1 || anchor != null) && (step < 2 || pixel != null);
        public bool IsReady => step == 3 && IsHealthy;
        public void InitializeNextStep()
        {
            switch (step)
            {
                case 0:
                    anchor = new GameObject("PreLuna.ThirdPersonHUD.PlaceholderAnchor");
                    anchorsCreated++;
                    anchor.SetActive(false); anchor.hideFlags = HideFlags.DontSave; break;
                case 1:
                    pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    texturesCreated++; pixel.name = "PreLuna.ThirdPersonHUD.PlaceholderPixel";
                    pixel.hideFlags = HideFlags.DontSave; pixel.filterMode = FilterMode.Point; break;
                case 2:
                    pixel.SetPixel(0, 0, Color.white); pixel.Apply(false, true); break;
                default: return;
            }
            step++;
        }
        public void Bind(ContextStamp nextContext, LocalViewBinding nextBinding)
        { Hide(); context = nextContext; binding = nextBinding; }
        public void Show(in HudPlacement next)
        {
            if (!IsReady || context != next.View.Context || binding != next.View.Binding) { Hide(); return; }
            var p = next.WorldAnchor;
            anchor.transform.position = new Vector3(p.X, p.Y, p.Z);
            placement = next; queued = true;
        }
        public void Hide() { queued = false; placement = default; }
        public void Draw()
        {
            if (!queued || !IsReady || Event.current == null || Event.current.type != EventType.Repaint) return;
            if (Camera.current != null && Camera.current.targetTexture != null) { textureRejections++; return; }
            float half = placement.SizePixels * .5f;
            var rect = new Rect(placement.PixelCenter.X - half, Screen.height - placement.PixelCenter.Y - half,
                placement.SizePixels, placement.SizePixels);
            var previous = GUI.color;
            try { GUI.color = Color.white; GUI.DrawTexture(rect, pixel); lastDraw = placement; draws++; }
            finally { GUI.color = previous; }
        }
        public void Release()
        {
            Hide(); var oldAnchor = anchor; var oldPixel = pixel;
            anchor = null; pixel = null; step = 0; context = default; binding = default;
            try { if (oldPixel != null) { UnityEngine.Object.Destroy(oldPixel); destroyRequests++; } }
            finally { if (oldAnchor != null) { UnityEngine.Object.Destroy(oldAnchor); destroyRequests++; } }
        }
    }
}
