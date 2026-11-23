using B2U;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class B2UPaletteClicker : MonoBehaviour, IPointerClickHandler
{
    public SceneController sceneController;
    public RawImage paletteImage;

    private Texture2D sliceTexture;

    public void OnPointerClick(PointerEventData eventData)
    {
        RectTransform rect = GetComponent<RectTransform>();

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
        {
            Vector2 normalizedPoint = Rect.PointToNormalized(rect.rect, localPoint);

            if (sceneController != null)
            {
                sceneController.UI_SetPaintIndexFromClick(normalizedPoint);
            }
        }
    }

    public void SetPalette(float value)
    {
        int index = Mathf.Clamp(Mathf.RoundToInt(value), 0, 15);

        if (B2URuntimeUtility.ActiveProfile.propertyBlocks.TryGetValue(B2UShaderProperty.PaintPalette, out var paletteBlock))
        {
            if (paletteBlock.value != null && paletteBlock.value is Texture2DArray palette)
            {
                // Create a standard, universally supported RGBA32 texture once
                if (sliceTexture == null || sliceTexture.width != palette.width || sliceTexture.height != palette.height)
                {
                    if (sliceTexture != null) Destroy(sliceTexture);

                    sliceTexture = new Texture2D(palette.width, palette.height, TextureFormat.RGBA32, false);
                    sliceTexture.filterMode = FilterMode.Point;
                }

                // Safely extract the slice pixels and apply them to the UI texture
                Color32[] pixels = palette.GetPixels32(index);
                sliceTexture.SetPixels32(pixels);
                sliceTexture.Apply();

                paletteImage.texture = sliceTexture;
            }
        }
    }

    private void OnDestroy()
    {
        if (sliceTexture != null)
        {
            Destroy(sliceTexture);
        }
    }
}