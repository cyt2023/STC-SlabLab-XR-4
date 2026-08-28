using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace EventAnchor
{
    [Serializable]
    public class RawAnchorsList
    {
        public RawAnchor[] anchorsList;
    }
    [Serializable]
    public class RawAnchor
    {
        public float centerX;
        public float centerY;
        //public float z;
        public float height;
        public float radius;
        public float min_z;
        public float max_z;
    }
    public class AnchorFactory : MonoBehaviour
    {
        public static RawAnchorsList importFromJson(string fileName)
        {
            // Json 文件的路径
            string json_src = File.ReadAllText(Application.streamingAssetsPath + "/" + fileName + ".json");
            RawAnchorsList rawAnchorList = JsonUtility.FromJson<RawAnchorsList>(json_src);
            return rawAnchorList;
        }
    }
}

