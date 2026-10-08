using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Brickcraft.World
{
    /// <summary>
    /// Packs the block textures into the Texture2DArray used by the terrain material,
    /// one layer per texture file. Layer 0 is a "missing texture" checkerboard.
    /// </summary>
    public class TerrainTextures
    {
        private class Layer
        {
            public int width;
            public int height;
            public Color32[] pixels;
        }

        private readonly List<Layer> layers = new List<Layer>();
        private readonly Dictionary<string, int> layersByPath = new Dictionary<string, int>();

        public const int MissingLayer = 0;

        public TerrainTextures() {
            layers.Add(createMissingLayer());
        }

        /// <summary>Adds a png/jpg and returns its layer, or <see cref="MissingLayer"/> if it can't be read.</summary>
        public int Add(string path) {
            if (layersByPath.TryGetValue(path, out int existing)) {
                return existing;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            int layer = MissingLayer;

            try {
                if (texture.LoadImage(File.ReadAllBytes(path))) {
                    layer = layers.Count;
                    layers.Add(new Layer() {
                        width = texture.width,
                        height = texture.height,
                        pixels = texture.GetPixels32(),
                    });
                } else {
                    Debug.LogError("Unsupported texture file " + path);
                }
            } catch (IOException e) {
                Debug.LogError("Can't read texture " + path + ": " + e.Message);
            } finally {
                Destroy(texture);
            }

            layersByPath[path] = layer;
            return layer;
        }

        /// <summary>All layers are scaled to the size of the biggest texture.</summary>
        public Texture2DArray Build() {
            int size = 1;
            foreach (Layer layer in layers) {
                size = Mathf.Max(size, layer.width, layer.height);
            }

            Texture2DArray array = new Texture2DArray(size, size, layers.Count, TextureFormat.RGBA32, true, false);
            array.name = "TerrainTextures";
            array.filterMode = FilterMode.Bilinear;
            array.wrapMode = TextureWrapMode.Repeat;

            for (int i = 0; i < layers.Count; i++) {
                array.SetPixels32(resize(layers[i], size), i, 0);
            }
            array.Apply(true, true);

            return array;
        }

        /// <summary>
        /// UV that makes the terrain shader (TriplanarTest) sample the given layer, the texture itself
        /// is projected from the world position. The shader computes the layer as
        /// round(4 * (u - 0.125)) + round(16 * (0.875 - v)), so u = 0.125 zeroes the first term and v
        /// gives the layer. Both terms land on whole numbers, far from a rounding boundary, so the
        /// small interpolation errors across a triangle can't flip pixels to a neighbour layer.
        /// </summary>
        public static Vector2 LayerToUV(int layer) {
            return new Vector2(0.125f, 0.875f - layer / 16f);
        }

        /// <summary>Destroys a texture both in play mode and when loading from editor code.</summary>
        public static void Destroy(Object texture) {
            if (Application.isPlaying) {
                Object.Destroy(texture);
            } else {
                Object.DestroyImmediate(texture);
            }
        }

        // nearest neighbour keeps pixel art crisp
        private static Color32[] resize(Layer layer, int size) {
            if (layer.width == size && layer.height == size) {
                return layer.pixels;
            }
            Color32[] result = new Color32[size * size];

            for (int y = 0; y < size; y++) {
                int sourceY = y * layer.height / size;
                for (int x = 0; x < size; x++) {
                    result[y * size + x] = layer.pixels[sourceY * layer.width + x * layer.width / size];
                }
            }
            return result;
        }

        private static Layer createMissingLayer() {
            const int size = 2;
            Color32 magenta = new Color32(255, 0, 255, 255);
            Color32 black = new Color32(0, 0, 0, 255);

            return new Layer() {
                width = size,
                height = size,
                pixels = new Color32[] { magenta, black, black, magenta },
            };
        }
    }
}
