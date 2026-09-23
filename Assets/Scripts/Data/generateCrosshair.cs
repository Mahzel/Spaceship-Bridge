#if UNITY_EDITOR
using UnityEditor;
#endif
using System.IO;
using UnityEngine;

public class CrosshairGenerator : MonoBehaviour
{
    public int textureSize = 512; // Taille de la texture (ajuste selon tes besoins)

    // Génère et sauvegarde les textures de la mire
    public void GenerateAndSaveCrosshairTextures()
    {
        Texture2D normalCrosshair = GenerateCrosshairTexture(textureSize, textureSize, false);
        Texture2D zoomCrosshair = GenerateCrosshairTexture(textureSize, textureSize, true);

        SaveTextureAsPNG(normalCrosshair, "NormalCrosshair");
        SaveTextureAsPNG(zoomCrosshair, "ZoomCrosshair");
    }

    // Génère une texture de mire
    private Texture2D GenerateCrosshairTexture(int width, int height, bool isZoomMode)
    {
        Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        // Remplir avec transparence totale
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(0, 0, 0, 0);
        }
        texture.SetPixels(pixels);

        int centerX = width / 2;
        int centerY = height / 2;
        int lineThickness = 2;
        Color lineColor = new(1, 0, 0, 0.3f); // Rouge semi-transparent

        if (isZoomMode)
        {
            // Mode zoom : croix simple
            DrawFullCross(texture, centerX, centerY, lineThickness, lineColor);
        }
        else
        {
            // Mode normal : croix avec carré central
            int crosshairSize = width / 4;
            int squareHalfSize = width / 20; // 1/10 de la taille

            // Ligne horizontale (s'arrête aux bords du carré)
            DrawHorizontalLines(texture, centerX, centerY, squareHalfSize, lineThickness, lineColor);
            // Ligne verticale (s'arrête aux bords du carré)
            DrawVerticalLines(texture, centerX, centerY, squareHalfSize, lineThickness, lineColor);
            // Carré central
            DrawSquare(texture, centerX, centerY, squareHalfSize, lineThickness, lineColor);
        }

        texture.Apply();
        return texture;
    }

    // Dessine une croix complète (pour le mode zoom)
    private void DrawFullCross(Texture2D texture, int centerX, int centerY, int lineThickness, Color lineColor)
    {
        // Ligne horizontale
        for (int dy = -lineThickness / 2; dy <= lineThickness / 2; dy++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                texture.SetPixel(x, centerY + dy, lineColor);
            }
        }

        // Ligne verticale
        for (int dx = -lineThickness / 2; dx <= lineThickness / 2; dx++)
        {
            for (int y = 0; y < texture.height; y++)
            {
                texture.SetPixel(centerX + dx, y, lineColor);
            }
        }
    }

    // Dessine les lignes horizontales (pour le mode normal)
    private void DrawHorizontalLines(Texture2D texture, int centerX, int centerY, int squareHalfSize, int lineThickness, Color lineColor)
    {
        for (int dy = -lineThickness / 2; dy <= lineThickness / 2; dy++)
        {
            // Partie gauche de la ligne horizontale
            for (int x = 0; x <= centerX - squareHalfSize; x++)
            {
                texture.SetPixel(x, centerY + dy, lineColor);
            }
            // Partie droite de la ligne horizontale
            for (int x = centerX + squareHalfSize; x < texture.width; x++)
            {
                texture.SetPixel(x, centerY + dy, lineColor);
            }
        }
    }

    // Dessine les lignes verticales (pour le mode normal)
    private void DrawVerticalLines(Texture2D texture, int centerX, int centerY, int squareHalfSize, int lineThickness, Color lineColor)
    {
        for (int dx = -lineThickness / 2; dx <= lineThickness / 2; dx++)
        {
            // Partie basse de la ligne verticale
            for (int y = 0; y <= centerY - squareHalfSize; y++)
            {
                texture.SetPixel(centerX + dx, y, lineColor);
            }
            // Partie haute de la ligne verticale
            for (int y = centerY + squareHalfSize; y < texture.height; y++)
            {
                texture.SetPixel(centerX + dx, y, lineColor);
            }
        }
    }

    // Dessine le carré central (pour le mode normal)
    private void DrawSquare(Texture2D texture, int centerX, int centerY, int squareHalfSize, int lineThickness, Color lineColor)
    {
        for (int dx = -lineThickness / 2; dx <= lineThickness / 2; dx++)
        {
            // Ligne supérieure du carré
            for (int x = centerX - squareHalfSize; x <= centerX + squareHalfSize; x++)
            {
                texture.SetPixel(x, centerY - squareHalfSize + dx, lineColor);
            }
            // Ligne inférieure du carré
            for (int x = centerX - squareHalfSize; x <= centerX + squareHalfSize; x++)
            {
                texture.SetPixel(x, centerY + squareHalfSize + dx, lineColor);
            }
        }

        for (int dy = -lineThickness / 2; dy <= lineThickness / 2; dy++)
        {
            // Ligne gauche du carré
            for (int y = centerY - squareHalfSize; y <= centerY + squareHalfSize; y++)
            {
                texture.SetPixel(centerX - squareHalfSize + dy, y, lineColor);
            }
            // Ligne droite du carré
            for (int y = centerY - squareHalfSize; y <= centerY + squareHalfSize; y++)
            {
                texture.SetPixel(centerX + squareHalfSize + dy, y, lineColor);
            }
        }
    }

    // Sauvegarde une texture en tant que PNG
    private void SaveTextureAsPNG(Texture2D texture, string fileName)
    {
#if UNITY_EDITOR
        byte[] bytes = texture.EncodeToPNG();
        string path = EditorUtility.SaveFilePanel("Save PNG", "", fileName, "png");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllBytes(path, bytes);
            Debug.Log("Texture sauvegardée : " + path);
        }
#endif
    }
}
