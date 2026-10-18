using System.Collections.Generic;
using Brickcraft.Bricks.Instructions;
using UnityEngine;
using UnityEngine.UI;

namespace Brickcraft.UI
{
    /// <summary>
    /// A page of building instructions, as its prefab (Resources/UI/InstructionPage) lays it out: a step (its parts
    /// in a box, each with how many, its number and the model so far), the cover (the model's name, the finished
    /// model, how many parts and steps) or the parts list (every part and how many). InstructionBook renders them.
    /// </summary>
    public class InstructionPage : MonoBehaviour
    {
        public const int Width = 1280;
        public const int Height = 960;

        [Header("Step")]
        public GameObject stepView;
        public Text stepNumber;
        /// <summary>The step's model, when it's a submodel ("Bed").</summary>
        public Text stepModel;
        public RectTransform stepParts;
        public RawImage stepImage;

        [Header("Cover")]
        public GameObject coverView;
        public Text coverTitle;
        public RawImage coverImage;
        public Text coverInfo;

        [Header("Parts list")]
        public GameObject partsView;
        public Text partsTitle;
        public RectTransform partsGrid;

        /// <summary>A part with how many (its IconArea/Icon and Count), copied into the parts boxes.</summary>
        public GameObject partTemplate;

        private readonly List<GameObject> shownParts = new List<GameObject>();

        public void ShowStep(InstructionStep step, string model) {
            show(stepView);
            stepNumber.text = step.number.ToString();
            stepModel.text = model ?? "";
            stepModel.gameObject.SetActive(!string.IsNullOrEmpty(model));
            setImage(stepImage, step.image);
            fill(stepParts, step.parts);
        }

        public void ShowCover(string title, Texture image, string info) {
            show(coverView);
            coverTitle.text = title;
            coverInfo.text = info;
            setImage(coverImage, image);
        }

        public void ShowParts(string title, IList<StepPart> parts) {
            show(partsView);
            partsTitle.text = title;
            fill(partsGrid, parts);
        }

        private void show(GameObject view) {
            stepView.SetActive(view == stepView);
            coverView.SetActive(view == coverView);
            partsView.SetActive(view == partsView);
        }

        // a picture at its own aspect (see its AspectRatioFitter)
        private static void setImage(RawImage image, Texture texture) {
            image.texture = texture;
            image.enabled = texture != null;
            AspectRatioFitter fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter != null && texture != null) {
                fitter.aspectRatio = (float)texture.width / texture.height;
            }
        }

        private void fill(RectTransform box, IList<StepPart> parts) {
            // gone at once: the page is captured this frame
            foreach (GameObject shown in shownParts) {
                DestroyImmediate(shown);
            }
            shownParts.Clear();
            partTemplate.SetActive(false);
            foreach (StepPart part in parts) {
                GameObject row = Instantiate(partTemplate, box);
                row.SetActive(true);
                setImage(row.transform.Find("IconArea/Icon").GetComponent<RawImage>(), part.icon);
                row.transform.Find("Count").GetComponent<Text>().text = part.count + "x";
                shownParts.Add(row);
            }
        }
    }
}
