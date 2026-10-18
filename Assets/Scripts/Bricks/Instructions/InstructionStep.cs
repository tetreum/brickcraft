using System.Collections.Generic;
using Brickcraft.Bricks.LDraw;
using UnityEngine;

namespace Brickcraft.Bricks.Instructions
{
    /// <summary>A step of a model's building instructions, rendered (see StepRenderer).</summary>
    public class InstructionStep
    {
        /// <summary>Its number, counting every model's steps (submodels' first).</summary>
        public int number;
        /// <summary>Whose step it is, and which of its steps.</summary>
        public LDrawSteps.Model model;
        public int index;
        /// <summary>The model as it is once the step is done, its new parts outlined (StepRenderer.Width x Height, transparent around it).</summary>
        public Texture2D image;
        /// <summary>What the step adds: a part (or a finished submodel) in a colour, and how many.</summary>
        public readonly List<StepPart> parts = new List<StepPart>();
    }

    /// <summary>A part of a step's list: what it is, in which colour, how many, and its picture.</summary>
    public class StepPart
    {
        /// <summary>Its LDraw part number, or the submodel's key (see LDrawSteps.Key).</summary>
        public string part;
        /// <summary>Its LDraw colour code.</summary>
        public int color;
        public int count;
        public bool isSubmodel;
        public Texture2D icon;
    }
}
