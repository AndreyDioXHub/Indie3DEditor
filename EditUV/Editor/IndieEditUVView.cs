using UnityEngine;

namespace Indie3DEditor.EditUV
{
    /// <summary>
    /// Maps UV space (U right, V up) to a GUI rect (Y down).
    /// </summary>
    public class IndieEditUVView
    {
        public Vector2 Pan = new Vector2(0.5f, 0.5f);
        public float Zoom = 1.2f;

        public Rect CanvasRect { get; private set; }

        public void SetCanvasRect(Rect rect)
        {
            CanvasRect = rect;
        }

        public float PixelsPerUv
        {
            get
            {
                float size = Mathf.Min(CanvasRect.width, CanvasRect.height);
                return size / Mathf.Max(Zoom, 0.01f);
            }
        }

        public Vector2 UvToGui(Vector2 uv)
        {
            float pp = PixelsPerUv;
            Vector2 center = CanvasRect.center;
            return new Vector2(
                center.x + (uv.x - Pan.x) * pp,
                center.y - (uv.y - Pan.y) * pp
            );
        }

        public Vector2 GuiToUv(Vector2 gui)
        {
            float pp = PixelsPerUv;
            Vector2 center = CanvasRect.center;
            return new Vector2(
                Pan.x + (gui.x - center.x) / pp,
                Pan.y - (gui.y - center.y) / pp
            );
        }

        public void FrameUvRect(Rect uvBounds, float padding = 0.15f)
        {
            if (uvBounds.width < 1e-5f && uvBounds.height < 1e-5f)
            {
                Pan = new Vector2(0.5f, 0.5f);
                Zoom = 1.2f;
                return;
            }

            float padX = Mathf.Max(uvBounds.width * padding, 0.05f);
            float padY = Mathf.Max(uvBounds.height * padding, 0.05f);
            float w = uvBounds.width + padX * 2f;
            float h = uvBounds.height + padY * 2f;
            Pan = uvBounds.center;
            Zoom = Mathf.Max(w, h);
        }

        public void ZoomAt(Vector2 guiPoint, float zoomDelta)
        {
            Vector2 uvBefore = GuiToUv(guiPoint);
            Zoom = Mathf.Clamp(Zoom * zoomDelta, 0.05f, 50f);
            Vector2 uvAfter = GuiToUv(guiPoint);
            Pan += uvBefore - uvAfter;
        }
    }
}
