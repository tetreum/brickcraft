using System.Collections.Generic;
using UnityEngine;

public class Brick
{
    public string id;
    public int type;
    public GameObject gameObject;

    public BrickModel model {
        get {
            return Server.brickModels[type];
        }
    }
}
