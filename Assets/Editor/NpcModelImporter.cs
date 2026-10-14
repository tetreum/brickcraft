using System.Collections.Generic;
using System.IO;
using Brickcraft.Models;
using Brickcraft.Npcs;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Turns an animated glTF model (like Blockbench's exports) into an NPC model prefab of the game:
/// Assets/Resources/NpcModels/[name].prefab (see Npcs.NpcModel), built by Npcs.NpcModelBuilder (what it
/// does with the model is there), with next to the glTF its meshes, textures, materials, an AnimationClip
/// per animation and an Animator controller with a state for each. NPCs of mods don't need it: a model.glb
/// or model.gltf in their folder is built the same way while the game runs.
/// </summary>
public static class NpcModelImporter
{
    public const string PrefabsFolder = "Assets/Resources/" + NpcInfo.ModelsFolder;

    [MenuItem("Brickcraft/Import NPC model (glTF)...")]
    public static void ImportFromMenu() {
        string file = EditorUtility.OpenFilePanelWithFilters("NPC model", "Assets/Models/NPCs", new[] { "glTF", "gltf,glb" });
        if (string.IsNullOrEmpty(file)) {
            return;
        }
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
        file = file.Replace('\\', '/');
        if (!file.StartsWith(project)) {
            EditorUtility.DisplayDialog("NPC model", "Pick a file inside the project's Assets.", "OK");
            return;
        }
        Import(file.Substring(project.Length));
    }

    /// <summary>Imports the model (a path like Assets/Models/NPCs/Golem/Golem.gltf), returns its prefab.</summary>
    public static GameObject Import(string path) {
        string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');
        NpcModelBuilder builder = NpcModelBuilder.Build(GltfFile.Load(path), name, false);
        GameObject root = builder.Root;
        try {
            saveTextures(builder, folder, name);
            saveMaterials(builder, folder);
            saveMeshes(builder, folder + "/" + name + "_meshes.asset");

            NpcModel model = root.GetComponent<NpcModel>();
            List<string> names = new List<string>(model.animations);
            for (int i = 0; i < builder.Clips.Count; i++) {
                AnimationClip clip = builder.Clips[i];
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = System.Array.IndexOf(NpcModelBuilder.LoopingAnimations, names[i].ToLowerInvariant()) >= 0;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                string clipPath = folder + "/" + name + "_" + names[i] + ".anim";
                AssetDatabase.DeleteAsset(clipPath);
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            Animator animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = makeController(folder + "/" + name + ".controller", builder.Clips, names);

            Directory.CreateDirectory(PrefabsFolder);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabsFolder + "/" + name + ".prefab");
            Debug.Log("Imported the NPC model " + name + ": " + root.GetComponentsInChildren<MeshRenderer>().Length + " parts, "
                + builder.Clips.Count + " animations (" + string.Join(", ", model.animations) + "), hitbox " + model.hitbox.size);
            return prefab;
        } finally {
            Object.DestroyImmediate(root);
        }
    }

    // as PNG files next to the model, unfiltered (pixel art); the materials then use those
    private static void saveTextures(NpcModelBuilder builder, string folder, string name) {
        for (int i = 0; i < builder.Textures.Count; i++) {
            Texture2D built = builder.Textures[i];
            string texturePath = folder + "/" + name + "_texture" + i + ".png";
            File.WriteAllBytes(texturePath, built.EncodeToPNG());
            AssetDatabase.ImportAsset(texturePath);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Texture2D saved = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            foreach (Material material in builder.Materials) {
                if (material.GetTexture("_BaseColorMap") == built) {
                    material.SetTexture("_BaseColorMap", saved);
                }
            }
        }
    }

    private static void saveMaterials(NpcModelBuilder builder, string folder) {
        foreach (Material material in builder.Materials) {
            UnityEditor.Rendering.HighDefinition.HDShaderUtils.ResetMaterialKeywords(material);
            string materialPath = folder + "/" + material.name + ".mat";
            AssetDatabase.DeleteAsset(materialPath);
            AssetDatabase.CreateAsset(material, materialPath);
        }
    }

    // one asset with every mesh
    private static void saveMeshes(NpcModelBuilder builder, string meshesPath) {
        AssetDatabase.DeleteAsset(meshesPath);
        for (int i = 0; i < builder.Meshes.Count; i++) {
            if (i == 0) {
                AssetDatabase.CreateAsset(builder.Meshes[i], meshesPath);
            } else {
                AssetDatabase.AddObjectToAsset(builder.Meshes[i], meshesPath);
            }
        }
        AssetDatabase.SaveAssets();
    }

    // a state per clip, played by name (see Npcs.NpcModel); idle first if there's one
    private static AnimatorController makeController(string path, List<AnimationClip> clips, List<string> names) {
        AssetDatabase.DeleteAsset(path);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        for (int i = 0; i < clips.Count; i++) {
            AnimatorState state = machine.AddState(names[i]);
            state.motion = clips[i];
            if (names[i].ToLowerInvariant() == "idle") {
                machine.defaultState = state;
            }
        }
        AssetDatabase.SaveAssets();
        return controller;
    }
}
