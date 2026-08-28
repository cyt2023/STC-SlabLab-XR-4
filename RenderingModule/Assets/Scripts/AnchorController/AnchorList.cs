using MapController;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityVolumeRendering;
namespace EventAnchor
{
    public class AnchorList : MonoBehaviour
    {
        public Anchor prefab;
        public List<Anchor> anchorList = new List<Anchor>();
        public LayerMask clickableLayer;
        public bool _debug_isFocusing = true;
        //  鼠标点击事件触发器
        private MapMouseTrigger mapMouseTrigger;
        UnityEngine.RaycastHit hit;
        private TClipper tClipper;
        private VolumeControllerObject volControlObj;
        private Anchor curCheckedAnchor;
        // Start is called before the first frame update
        void Start()
        {
            volControlObj = FindObjectOfType<VolumeControllerObject>();
            tClipper = FindObjectOfType<TClipper>();
            foreach (var rawAnchor in AnchorFactory.importFromJson("anchorList_new_Test100_100_4416_processed").anchorsList)
            {
                Anchor anchor = Instantiate(prefab, transform);
                anchor.init(rawAnchor.centerX, rawAnchor.centerY, rawAnchor.height, rawAnchor.radius, rawAnchor.min_z + volControlObj.GetBottomY(), rawAnchor.max_z + volControlObj.GetBottomY());
                anchor.setScale(new Vector3(anchor.radius * 10 * 2, anchor.height / 2, anchor.radius * 2 * 10));
                anchorList.Add(anchor);
            }
            //transform.GetComponentsInChildren<Anchor>(anchorList);
            mapMouseTrigger = FindAnyObjectByType<MapMouseTrigger>();

            refreshAnchors();
        }

        // Update is called once per frame
        void Update()
        {
            //  按下ESC，强制取消所有锚点的选中状态
            if ((Input.GetKeyDown(KeyCode.Escape)) && curCheckedAnchor != null)
            {
                setAllAnchorState(AnchorState.Unchecked);
                curCheckedAnchor = null;
                if (_debug_isFocusing)
                {
                    Camera.main.GetComponent<CameraController>().resetCamera();
                }
                
                refreshAnchors();
            }
            if (tClipper.isDragging()) return;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out hit, 1000, clickableLayer.value) && Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject() && !mapMouseTrigger.isSetHighlightPosition)
            {
                if (hit.collider.gameObject.GetComponent<Anchor>() != null)
                {
                    curCheckedAnchor = hit.collider.gameObject.GetComponent<Anchor>();
                    if (curCheckedAnchor.state == AnchorState.Inactive) return;
                    if (curCheckedAnchor.state == AnchorState.Unchecked)
                    {
                        setAllAnchorState(AnchorState.Unchecked);
                        curCheckedAnchor.setState(AnchorState.Checked);
                        if (_debug_isFocusing)
                        {
                            Camera.main.GetComponent<CameraController>().focusObject(curCheckedAnchor.gameObject);
                        }
                        
                        refreshAnchors();
                    }
                    else
                    {
                        curCheckedAnchor.setState(AnchorState.Unchecked);
                        curCheckedAnchor = null;
                        if (_debug_isFocusing)
                        {
                            Camera.main.GetComponent<CameraController>().resetCamera();
                        }
                        
                        refreshAnchors();
                    }
                }
            }
        }

        public void SetActiveByTimeRange(float startTime, float endTime)
        {
            foreach (Anchor anchor in anchorList)
            {
                if (anchor.state == AnchorState.Checked) continue;
                if (anchor.startTime > endTime || anchor.endTime < startTime)
                {
                    anchor.state = AnchorState.Inactive;
                    anchor.gameObject.SetActive(false);
                }
                else
                {
                    anchor.gameObject.SetActive(true);
                    anchor.state = AnchorState.Unchecked;
                    
                }
            }
        }

        private bool isOneChecked()
        {
            bool isChecked = false;
            foreach (Anchor anchor in anchorList)
            {
                if (anchor.state == AnchorState.Checked)
                {
                    isChecked = true;
                    break;
                }
            }
            return isChecked;
        }

        private void setAllAnchorState(AnchorState state)
        {
            foreach (Anchor anchor in anchorList)
            {
                anchor.setState(state);
            }
        }

        void refreshAnchors()
        {
            foreach (Anchor item in anchorList)
            {
                if (item.transform.position.x != item.position.x || item.transform.position.z != item.position.y || item.transform.position.y != item.position.z)
                {
                    Vector3 position = new Vector3(item.position.x * 10 - 5, item.startTime + item.height / 2, item.position.y * 10 - 5);
                    item.transform.position = position;
                }
                if (item.state == AnchorState.Checked)
                {
                    //  设置被选中锚点的颜色为红色
                    MeshRenderer meshRendererOfAnchor = item.transform.GetChild(0).gameObject.GetComponent<MeshRenderer>();
                    Material material = new Material(meshRendererOfAnchor.sharedMaterial);
                    material.SetColor("_Color", new Color(1, 0, 0.1431785f, 0.5176471f));
                    meshRendererOfAnchor.sharedMaterial = material;
                    //  裁切对应位置
                    tClipper.setClipWindowByTime((item.startTime), (item.endTime));
                    //  高亮对应位置
                    volControlObj.SetHighlightPosition(new Vector2(item.position.x, item.position.y));
                    volControlObj.SetHighlightRadius(item.radius);
                }
                else
                {
                    //  还原锚点颜色为黄色
                    MeshRenderer meshRenderer = item.transform.GetChild(0).gameObject.GetComponent<MeshRenderer>();
                    Material material = new Material(meshRenderer.sharedMaterial);
                    material.SetColor("_Color", new Color(1, 0.9307095f, 0, 0.5176471f));
                    meshRenderer.sharedMaterial = material;
                }
            }
            if (!isOneChecked())
            {
                tClipper.resetAll();
                volControlObj.SetHighlightPosition(new Vector2(0.5f, 0.5f));
                volControlObj.SetHighlightRadius(0.707f);
            }
        }
    }
}

