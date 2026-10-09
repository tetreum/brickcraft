using System.Collections.Generic;
using System.IO;
using System.Text;
using Brickcraft.Bricks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates a material per colour of Assets/colors.csv (Rebrickable's colour list: id, name, rgb, is_trans, ...)
/// and the BrickColorPalette that finds them by id. Running it again only updates the colour (and finish)
/// of existing materials, other tweaks made in the editor are kept.
/// </summary>
public static class BrickColorMaterialsGenerator
{
    private const string CsvPath = "Assets/colors.csv";
    private const string Folder = "Assets/Materials/BrickColors/Palette";
    private const string OpaqueTemplate = "Assets/Materials/BrickColors/BrightGreen.mat";
    private const string TransparentTemplate = "Assets/Materials/BrickColors/TransparentBlue.mat";
    private const string PalettePath = "Assets/Resources/" + BrickColorPalette.ResourceName + ".asset";

    // how see-through transparent bricks are
    private const float TransparentAlpha = 0.55f;

    // Rebrickable's "[Unknown]" and "[No Color/Any Color]" aren't colours a brick can have
    private static readonly HashSet<int> Skipped = new HashSet<int>() { -1, 9999 };

    [MenuItem("Brickcraft/Generate brick colour materials")]
    public static void Generate() {
        Material opaque = AssetDatabase.LoadAssetAtPath<Material>(OpaqueTemplate);
        Material transparent = AssetDatabase.LoadAssetAtPath<Material>(TransparentTemplate);
        if (opaque == null || transparent == null) {
            Debug.LogError("Missing the template materials " + OpaqueTemplate + " and " + TransparentTemplate);
            return;
        }
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(Path.GetDirectoryName(PalettePath));

        List<BrickColor> colors = new List<BrickColor>();
        AssetDatabase.StartAssetEditing();
        try {
            foreach (string[] row in readCsv(CsvPath)) {
                int id;
                Color color;
                if (!int.TryParse(row[0], out id) || Skipped.Contains(id) || !ColorUtility.TryParseHtmlString("#" + row[2], out color)) {
                    continue;
                }
                bool isTransparent = row[3] == "True";
                string path = Folder + "/" + id + "_" + fileName(row[1]) + ".mat";

                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) {
                    material = new Material(isTransparent ? transparent : opaque);
                    AssetDatabase.CreateAsset(material, path);
                }
                applyColor(material, row[1], color, isTransparent);
                EditorUtility.SetDirty(material);

                colors.Add(new BrickColor() { id = id, name = row[1], hex = row[2], isTransparent = isTransparent, material = material });
            }
        } finally {
            AssetDatabase.StopAssetEditing();
        }

        BrickColorPalette palette = AssetDatabase.LoadAssetAtPath<BrickColorPalette>(PalettePath);
        if (palette == null) {
            palette = ScriptableObject.CreateInstance<BrickColorPalette>();
            AssetDatabase.CreateAsset(palette, PalettePath);
        }
        palette.colors = colors;
        EditorUtility.SetDirty(palette);
        AssetDatabase.SaveAssets();

        Debug.Log("Generated " + colors.Count + " brick colour materials in " + Folder);
    }

    private static void applyColor(Material material, string name, Color color, bool isTransparent) {
        color.a = isTransparent ? TransparentAlpha : 1;
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);

        // the finish, from the name
        float metallic = 0, smoothness = 0.5f;
        if (name.StartsWith("Chrome")) {
            metallic = 1;
            smoothness = 0.95f;
        } else if (name.Contains("Metallic") || name.Contains("Gold") || name.Contains("Silver") || name.Contains("Copper") || name.StartsWith("Pearl Titanium")) {
            metallic = 0.8f;
            smoothness = 0.7f;
        } else if (name.StartsWith("Pearl")) {
            metallic = 0.5f;
            smoothness = 0.8f;
        } else if (name.StartsWith("Glitter") || name.StartsWith("Opal")) {
            smoothness = 0.8f;
        }
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
    }

    // "Trans-Dark Blue" -> "TransDarkBlue"
    private static string fileName(string name) {
        StringBuilder result = new StringBuilder();
        foreach (char c in name) {
            if (char.IsLetterOrDigit(c)) {
                result.Append(c);
            }
        }
        return result.ToString();
    }

    // Rebrickable's CSV has no quoted fields, a header and one colour per line
    private static IEnumerable<string[]> readCsv(string path) {
        string[] lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++) {
            if (lines[i].Trim().Length > 0) {
                yield return lines[i].Split(',');
            }
        }
    }
}
