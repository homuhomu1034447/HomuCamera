using System;
using System.Collections.Generic;
using UnityEngine;
using UnityInjector;
using UnityInjector.Attributes;

namespace COM3D2.HomuCamera.Plugin
{
    [PluginFilter("COM3D2x64"),
     PluginName("COM3D2 HomuCamera"),
     PluginVersion("0.0.0.1")]
    public class HomuCamera : PluginBase
    {
        private bool screenCreated = false;
        private Rect winRect;
        private PixelValues pv;
        private float guiWidth = 0.4f;
        private Vector2 lastScreenSize;
        MenuType menuType = MenuType.None;
        private Maid maid;
        private GameObject myCamBase;
        private float cameraRotate = 0f;
        private float cameraFov = 36f;

        private Vector2 cameraObjectScrollViewVector = Vector2.zero;
        private Vector2 cameraMaterialScrollViewVector = Vector2.zero;
        private Vector2 cameraNormalScrollViewVector = Vector2.zero;

        private Vector2 displayObjectScrollViewVector = Vector2.zero;
        private Vector2 displayMaterialScrollViewVector = Vector2.zero;

        private Dictionary<string, GameObject> gameObjects = new Dictionary<string, GameObject>();
        private Dictionary<string, HomuTarget> cameraMaterials = new Dictionary<string, HomuTarget>();
        private Dictionary<string, HomuTarget2> cameraNormals = new Dictionary<string, HomuTarget2>();
        private Dictionary<string, HomuTarget> displayMaterials = new Dictionary<string, HomuTarget>();

        private string currentCameraGameObject = null;
        private string currentCameraMaterial = null;
        private string currentCameraNormal = null;
        private string currentDisplayGameObject = null;
        private string currentDisplayMaterial = null;


        private enum TargetLevel
        {
            // エディット
            SceneEdit = 5,
        }

        private enum MenuType
        {
            None,
            Main
        }

        private class PixelValues
        {
            public float BaseWidth = 1280f;
            public float PropRatio = 0.6f;
            public int Margin;

            private Dictionary<string, int> font = new Dictionary<string, int>();
            private Dictionary<string, int> line = new Dictionary<string, int>();
            private Dictionary<string, int> sys = new Dictionary<string, int>();


            public PixelValues()
            {
                Margin = PropPx(10);

                font["C1"] = 11;
                font["C2"] = 12;
                font["H1"] = 14;
                font["H2"] = 16;
                font["H3"] = 20;

                line["C1"] = 14;
                line["C2"] = 18;
                line["H1"] = 22;
                line["H2"] = 24;
                line["H3"] = 30;

                sys["Menu.Height"] = 45;
                sys["OkButton.Height"] = 95;

                sys["HScrollBar.Width"] = 15;
            }

            public int Font(string key)
            {
                return PropPx(font[key]);
            }

            public int Line(string key)
            {
                return PropPx(line[key]);
            }

            public int Sys(string key)
            {
                return PropPx(sys[key]);
            }

            public int Font_(string key)
            {
                return font[key];
            }

            public int Line_(string key)
            {
                return line[key];
            }

            public int Sys_(string key)
            {
                return sys[key];
            }

            public Rect PropScreen(float left, float top, float width, float height)
            {
                return new Rect((int)((Screen.width - Margin * 2) * left + Margin)
                    , (int)((Screen.height - Margin * 2) * top + Margin)
                    , (int)((Screen.width - Margin * 2) * width)
                    , (int)((Screen.height - Margin * 2) * height));
            }

            public Rect PropScreenMH(float left, float top, float width, float height)
            {
                Rect r = PropScreen(left, top, width, height);
                r.y += Sys("Menu.Height");
                r.height -= (Sys("Menu.Height") + Sys("OkButton.Height"));

                return r;
            }

            public Rect PropScreenMH(float left, float top, float width, float height, Vector2 last)
            {
                Rect r = PropScreen((float)(left / (last.x - Margin * 2)), (float)(top / (last.y - Margin * 2)), width,
                    height);
                r.height -= (Sys("Menu.Height") + Sys("OkButton.Height"));

                return r;
            }

            public Rect InsideRect(Rect rect)
            {
                return new Rect(Margin, Margin, rect.width - Margin * 2, rect.height - Margin * 2);
            }

            public Rect InsideRect(Rect rect, int height)
            {
                return new Rect(Margin, Margin, rect.width - Margin * 2, height);
            }

            public int PropPx(int px)
            {
                return (int)(px * (1f + (Screen.width / BaseWidth - 1f) * PropRatio));
            }
        }

        private Dictionary<string, GameObject> searchObj()
        {
            Dictionary<string, GameObject> result = new Dictionary<string, GameObject>();


            var i = 0;
            foreach (GameObject gameObject in FindObjectsOfType<GameObject>())
            {
                if (gameObject.name.Length >= 6 && gameObject.name.ToLower().EndsWith(".menu"))
                {
                    result.Add($"[{i}]{gameObject.name}", gameObject);
                }

                i++;
            }

            return result;
        }

        private Dictionary<string, HomuTarget> searchMaterial(GameObject gameObject)
        {
            Dictionary<string, HomuTarget> result = new Dictionary<string, HomuTarget>();


            var i = 0;
            foreach (SkinnedMeshRenderer skinnedMeshRenderer in gameObject.transform
                         .GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                for (int k = 0; k < skinnedMeshRenderer.sharedMaterials.Length; k++)
                {
                    if (skinnedMeshRenderer != null)
                    {
                        var mat = skinnedMeshRenderer.sharedMaterials[k];
                        var data = new HomuTarget();
                        data.renderer = skinnedMeshRenderer;
                        data.material = mat;
                        data.gameObject = gameObject;
                        data.materialIndex = k;
                        result.Add($"[{i}-{k}]{mat.name}", data);
                    }
                }

                i++;
            }

            return result;
        }

        private Dictionary<string, HomuTarget2> searchNormal(HomuTarget target)
        {
            // サブメッシュなし
            if (target.renderer.sharedMesh.subMeshCount == 0)
            {
                Debug.Log("サブメッシュなしは未対応");

                return new Dictionary<string, HomuTarget2>();
            }
            else
            {
                //メッシュからサブメッシュを取得して指定のマテリアルの頂点だけ取り出す
                var triangles = target.renderer.sharedMesh.GetTriangles(target.materialIndex);
                // ３点から法線を計算する
                // https://docs.unity3d.com/ja/2019.4/Manual/ComputingNormalPerpendicularVector.html

                Vector3[] meshVertices = target.renderer.sharedMesh.vertices;

                var vector3s = new HashSet<Vector3>();

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    var a = meshVertices[triangles[i]];
                    var b = meshVertices[triangles[i + 1]];
                    var c = meshVertices[triangles[i + 2]];

                    var side1 = b - a;
                    var side2 = c - a;

                    var perp = Vector3.Cross(side1, side2).normalized;

                    var r = 0.01;

                    var normal = new Vector3(
                        perp.x > -r && perp.x < r ? 0 : perp.x,
                        perp.y > -r && perp.y < r ? 0 : perp.y,
                        perp.z > -r && perp.z < r ? 0 : perp.z);

                    vector3s.Add(normal);
                }

                Debug.Log("calc nomarls");

                var result = new Dictionary<string, HomuTarget2>();
                foreach (var n in vector3s)
                {
                    var r = new HomuTarget2();
                    r.target = target;
                    r.normal = n;
                    var key = $"{n.x}-{n.y}-{n.z}";
                    if (!result.ContainsKey(key))
                    {
                        result.Add(key, r);
                    }
                }

                return result;
            }
        }

        private void setCamera(HomuTarget2 target)
        {
            // カメラを作成
            if (myCamBase != null)
            {
                Destroy(myCamBase);
            }

            myCamBase = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            myCamBase.GetComponent<Renderer>().enabled = false;
            myCamBase.transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
            myCamBase.transform.SetParent(target.target.gameObject.transform);

            // object保存時の角度を読み出す
            var gAngle = target.target.gameObject.transform.eulerAngles;

            //メッシュからサブメッシュを取得して指定のマテリアルの頂点だけ取り出す
            var triangles = target.target.renderer.sharedMesh.GetTriangles(target.target.materialIndex);
            // 最初の３点から法線を計算する
            // https://docs.unity3d.com/ja/2019.4/Manual/ComputingNormalPerpendicularVector.html

            Vector3[] meshVertices = target.target.renderer.sharedMesh.vertices;

            // submeshのboundを求める
            var xMin = 1000f;
            var xMax = -1000f;
            var yMin = 1000f;
            var yMax = -1000f;
            var zMin = 1000f;
            var zMax = -1000f;

            for (var x = 0; x < triangles.Length; x++)
            {
                var pp = meshVertices[triangles[x]];
                if (pp != null)
                {
                    if (xMin > pp.x) xMin = pp.x;
                    if (yMin > pp.y) yMin = pp.y;
                    if (zMin > pp.z) zMin = pp.z;
                    if (xMax < pp.x) xMax = pp.x;
                    if (yMax < pp.y) yMax = pp.y;
                    if (zMax < pp.z) zMax = pp.z;
                }
            }

            // Grobalを初期化
            target.target.gameObject.transform.eulerAngles = Vector3.zero;

            //補正
            myCamBase.transform.forward = new Vector3(target.normal.x, target.normal.z, -target.normal.y);

            // カメラの下を決定
            myCamBase.transform.Rotate(myCamBase.transform.forward, cameraRotate);

            // 中心に設定
            myCamBase.transform.localPosition = new Vector3((xMax - xMin) / 2f + xMin, (zMax - zMin) / 2f + zMin,
                -((yMax - yMin) / 2f + yMin));

            Debug.Log(myCamBase.transform.forward);


            // Globalを戻す
            target.target.gameObject.transform.eulerAngles = gAngle;
        }

        private void setDisplay(HomuTarget target)
        {
            if (target.renderer.sharedMesh.subMeshCount == 0)
            {
                Debug.Log("サブメッシュなしは未対応");
            }
            else
            {
                //メッシュからサブメッシュを取得して指定のマテリアルの頂点だけ取り出す
                var triangles = target.renderer.sharedMesh.GetTriangles(target.materialIndex);

                var uvs = target.renderer.sharedMesh.uv;

                // submeshのboundを求める
                var xMin = 1000f;
                var xMax = -1000f;
                var yMin = 1000f;
                var yMax = -1000f;

                for (var x = 0; x < triangles.Length; x++)
                {
                    var u = uvs[triangles[x]];
                    if (u != null)
                    {
                        if (xMin > u.x) xMin = u.x;
                        if (yMin > u.y) yMin = u.y;
                        if (xMax < u.x) xMax = u.x;
                        if (yMax < u.y) yMax = u.y;
                    }
                }

                var tex = target.material.mainTexture;
                var eTex = new RenderTexture(tex.width, tex.height, 24);

                if (myCamBase != null)
                {
                    Camera cam;
                    cam = myCamBase.AddComponent<Camera>();
                    cam.enabled = true;
                    cam.depth = 9;
                    cam.nearClipPlane = 0.01f;
                    cam.farClipPlane = 1000f;
                    cam.rect = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
                    cam.transform.SetParent(myCamBase.transform);
                    cam.fieldOfView = cameraFov;
                    cam.targetTexture = eTex;
                }

                target.material.mainTexture = eTex;
            }
        }

        class HomuTarget
        {
            public GameObject gameObject;
            public Material material;
            public SkinnedMeshRenderer renderer;
            public int materialIndex;
        }

        class HomuTarget2
        {
            public HomuTarget target;
            public Vector3 normal;
        }

        private void DoMainMenu(int winID)
        {
            GUIStyle lStyle = "label";
            GUIStyle tStyle = "toggle";
            GUIStyle bStyle = "button";
            GUIStyle sBStyle = new GUIStyle();

            Color color = new Color(1f, 1f, 1f, 0.98f);
            Color selectedColor = new Color(0f, 0f, 1f, 0.98f);

            lStyle.normal.textColor = color;
            tStyle.normal.textColor = color;
            bStyle.normal.textColor = color;
            sBStyle.normal.textColor = selectedColor;
            var lStyleLineHeight = pv.Line("H3");
            var bStyleLineHeight = pv.Line("C1");
            lStyle.fontSize = pv.Font("H3");
            bStyle.fontSize = pv.Font("C1");
            sBStyle.fontSize = pv.Font("C1");
            lStyle.alignment = TextAnchor.UpperLeft;
            tStyle.alignment = TextAnchor.UpperLeft;
            bStyle.alignment = TextAnchor.UpperLeft;
            sBStyle.alignment = TextAnchor.UpperLeft;

            var baseRect = pv.InsideRect(this.winRect);

            var objectSearchButton = new Rect(
                baseRect.x,
                baseRect.y,
                baseRect.width + pv.PropPx(5),
                bStyleLineHeight
            );

            var cameraHeaderRect = new Rect(
                baseRect.x,
                baseRect.y + objectSearchButton.height + pv.Margin,
                baseRect.width / 2,
                lStyleLineHeight);

            var displayHeaderRect = new Rect(
                baseRect.x + baseRect.width / 2 + pv.PropPx(5),
                baseRect.y + objectSearchButton.height + pv.Margin,
                baseRect.width / 2,
                lStyleLineHeight);

            var cameraObjectScrollRect = new Rect(
                baseRect.x,
                baseRect.y + objectSearchButton.height + pv.Margin + cameraHeaderRect.height + pv.Margin,
                baseRect.width / 2,
                300);

            var displayObjectScrollRect = new Rect(
                baseRect.x + baseRect.width / 2 + pv.PropPx(5),
                baseRect.y + objectSearchButton.height + pv.Margin + cameraHeaderRect.height + pv.Margin,
                baseRect.width / 2,
                300);

            var cameraObjectInnerRect = new Rect(
                0,
                0,
                cameraObjectScrollRect.width - pv.Sys_("HScrollBar.Width") - pv.Margin,
                1200);

            var displayObjectInnerRect = new Rect(
                0,
                0,
                cameraObjectScrollRect.width - pv.Sys_("HScrollBar.Width") - pv.Margin,
                1200);

            var cameraMatericalRect = new Rect(
                baseRect.x,
                baseRect.y + objectSearchButton.height
                           + pv.Margin + cameraHeaderRect.height
                           + pv.Margin + cameraObjectScrollRect.height
                           + pv.Margin,
                baseRect.width / 2,
                300);

            var cameraMaterialInnerRect = new Rect(
                0,
                0,
                cameraMatericalRect.width - pv.Sys_("HScrollBar.Width") - pv.Margin,
                1200);

            var displayMatericalRect = new Rect(
                baseRect.x + baseRect.width / 2 + pv.PropPx(5),
                baseRect.y + objectSearchButton.height
                           + pv.Margin + displayHeaderRect.height
                           + pv.Margin + displayObjectScrollRect.height
                           + pv.Margin,
                baseRect.width / 2,
                300);

            var displayMaterialInnerRect = new Rect(
                0,
                0,
                displayMatericalRect.width - pv.Sys_("HScrollBar.Width") - pv.Margin,
                1200);

            var cameraNormalRect = new Rect(
                baseRect.x,
                baseRect.y + objectSearchButton.height
                           + pv.Margin + cameraHeaderRect.height
                           + pv.Margin + cameraObjectScrollRect.height
                           + pv.Margin + cameraMatericalRect.height
                           + pv.Margin,
                baseRect.width / 2,
                300);

            var cameraNormalInnerRect = new Rect(
                0,
                0,
                cameraNormalRect.width - pv.Sys_("HScrollBar.Width") - pv.Margin,
                1200);

            var currentCameraTargetRect = new Rect(
                baseRect.x,
                baseRect.y + objectSearchButton.height
                           + pv.Margin + cameraHeaderRect.height
                           + pv.Margin + cameraObjectScrollRect.height
                           + pv.Margin + cameraMatericalRect.height
                           + pv.Margin + cameraNormalRect.height
                           + pv.Margin,
                baseRect.width / 2,
                lStyleLineHeight);

            var currentDisplayTargetRect = new Rect(
                baseRect.x + baseRect.width / 2 + pv.PropPx(5),
                baseRect.y + objectSearchButton.height
                           + pv.Margin + cameraHeaderRect.height
                           + pv.Margin + cameraObjectScrollRect.height
                           + pv.Margin + cameraMatericalRect.height
                           + pv.Margin + cameraNormalRect.height
                           + pv.Margin,
                baseRect.width / 2,
                lStyleLineHeight);

            var cameraRotateSliderRect = new Rect(
                baseRect.x,
                baseRect.y + objectSearchButton.height
                           + pv.Margin + cameraHeaderRect.height
                           + pv.Margin + cameraObjectScrollRect.height
                           + pv.Margin + cameraMatericalRect.height
                           + pv.Margin + cameraNormalRect.height
                           + pv.Margin + currentCameraTargetRect.height
                           + pv.Margin,
                baseRect.width / 2,
                bStyleLineHeight);

            var cameraFovSliderRect = new Rect(
                baseRect.x,
                baseRect.y + objectSearchButton.height
                           + pv.Margin + cameraHeaderRect.height
                           + pv.Margin + cameraObjectScrollRect.height
                           + pv.Margin + cameraMatericalRect.height
                           + pv.Margin + cameraNormalRect.height
                           + pv.Margin + currentCameraTargetRect.height
                           + pv.Margin + cameraRotateSliderRect.height
                           + pv.Margin,
                baseRect.width / 2,
                bStyleLineHeight);

            var setButtonRect = new Rect(
                baseRect.x,
                baseRect.y + objectSearchButton.height
                           + pv.Margin + cameraHeaderRect.height
                           + pv.Margin + cameraObjectScrollRect.height
                           + pv.Margin + cameraMatericalRect.height
                           + pv.Margin + cameraNormalRect.height
                           + pv.Margin + currentCameraTargetRect.height
                           + pv.Margin + cameraRotateSliderRect.height
                           + pv.Margin + cameraFovSliderRect.height
                           + pv.Margin,
                baseRect.width,
                bStyleLineHeight);


            // header
            GUI.Label(cameraHeaderRect, "Camera", lStyle);
            GUI.Label(displayHeaderRect, "Display", lStyle);

            // オブジェクト検索ボタン
            if (GUI.Button(objectSearchButton, "Objectをサーチ", bStyle))
            {
                gameObjects = searchObj();
                cameraMaterials.Clear();
                cameraNormals.Clear();
                displayMaterials.Clear();
                currentCameraGameObject = null;
                currentCameraMaterial = null;
                currentCameraNormal = null;
                currentDisplayGameObject = null;
                currentDisplayMaterial = null;
            }

            // Camera Objectスクロールビュー
            cameraObjectScrollViewVector =
                GUI.BeginScrollView(cameraObjectScrollRect, cameraObjectScrollViewVector, cameraObjectInnerRect, false,
                    true);

            var innerTargetRect = new Rect();
            innerTargetRect.Set(0, 0, cameraObjectInnerRect.width, bStyleLineHeight);
            foreach (var pair in gameObjects)
            {
                var style = pair.Key == currentCameraGameObject ? sBStyle : "button";

                if (GUI.Button(innerTargetRect, $"[Object] {pair.Key}", style))
                {
                    cameraMaterials = searchMaterial(pair.Value);
                    currentCameraGameObject = pair.Key;
                    cameraNormals.Clear();
                    currentCameraMaterial = null;
                    currentCameraNormal = null;
                }

                innerTargetRect.y += bStyleLineHeight;
            }

            GUI.EndScrollView();

            // Display Objectスクロールビュー
            displayObjectScrollViewVector =
                GUI.BeginScrollView(displayObjectScrollRect, displayObjectScrollViewVector, displayObjectInnerRect,
                    false,
                    true);

            innerTargetRect.Set(0, 0, displayObjectInnerRect.width, bStyleLineHeight);
            foreach (var pair in gameObjects)
            {
                var style = pair.Key == currentDisplayGameObject ? sBStyle : "button";

                if (GUI.Button(innerTargetRect, $"[Object] {pair.Key}", style))
                {
                    displayMaterials = searchMaterial(pair.Value);
                    currentDisplayGameObject = pair.Key;
                }

                innerTargetRect.y += bStyleLineHeight;
            }

            GUI.EndScrollView();

            // Camera Materialスクロールビュー
            cameraMaterialScrollViewVector =
                GUI.BeginScrollView(cameraMatericalRect, cameraMaterialScrollViewVector, cameraMaterialInnerRect, false,
                    true);

            innerTargetRect.Set(0, 0, cameraMaterialInnerRect.width, bStyleLineHeight);
            foreach (var pair in cameraMaterials)
            {
                var style = pair.Key == currentCameraMaterial ? sBStyle : "button";

                if (GUI.Button(innerTargetRect, $"[Material] {pair.Key}", style))
                {
                    cameraNormals = searchNormal(pair.Value);
                    currentCameraMaterial = pair.Key;
                    currentCameraNormal = null;
                }

                innerTargetRect.y += bStyleLineHeight;
            }

            GUI.EndScrollView();

            // display Materialスクロールビュー
            displayMaterialScrollViewVector =
                GUI.BeginScrollView(displayMatericalRect, displayMaterialScrollViewVector, displayMaterialInnerRect,
                    false,
                    true);

            innerTargetRect.Set(0, 0, displayMaterialInnerRect.width, bStyleLineHeight);
            foreach (var pair in displayMaterials)
            {
                var style = pair.Key == currentDisplayMaterial ? sBStyle : "button";

                if (GUI.Button(innerTargetRect, $"[Material] {pair.Key}", style))
                {
                    currentDisplayMaterial = pair.Key;
                }

                innerTargetRect.y += bStyleLineHeight;
            }

            GUI.EndScrollView();

            // Camera Normalスクロールビュー
            cameraNormalScrollViewVector =
                GUI.BeginScrollView(cameraNormalRect, cameraNormalScrollViewVector, cameraNormalInnerRect, false,
                    true);

            innerTargetRect.Set(0, 0, cameraNormalInnerRect.width, bStyleLineHeight);
            foreach (var pair in cameraNormals)
            {
                var style = pair.Key == currentCameraNormal ? sBStyle : "button";

                if (GUI.Button(innerTargetRect, $"[Normal] {pair.Key}", style))
                {
                    currentCameraNormal = pair.Key;
                }

                innerTargetRect.y += bStyleLineHeight;
            }

            GUI.EndScrollView();

            GUI.Label(currentCameraTargetRect, currentCameraNormal != null ? "設定済み" : "未設定", lStyle);
            GUI.Label(currentDisplayTargetRect, currentDisplayMaterial != null ? "設定済み" : "未設定", lStyle);

            // カメラ回転
            cameraRotate = GUI.HorizontalSlider(cameraRotateSliderRect, cameraRotate, 0, 360);

            // カメラFOV

            cameraFov = GUI.HorizontalSlider(cameraFovSliderRect, cameraFov, 0, 100);

            // 設定ボタン
            if (currentCameraNormal != null && currentDisplayMaterial != null)
            {
                if (GUI.Button(setButtonRect, "設定", bStyle))
                {
                    setCamera(cameraNormals[currentCameraNormal]);
                    setDisplay(displayMaterials[currentDisplayMaterial]);
                }
            }


            GUI.DragWindow();
        }

        // -------- //

        private void Awake()
        {
            pv = new PixelValues();
            lastScreenSize = new Vector2(Screen.width, Screen.height);
        }

        private void OnLevelWasLoaded(int level)
        {
            if (!Enum.IsDefined(typeof(TargetLevel), level)) return;
            menuType = MenuType.None;
            screenCreated = false;
            winRect = pv.PropScreenMH(1f - guiWidth, 0f, guiWidth, 1f);
        }

        private void Update()
        {
            if (!Enum.IsDefined(typeof(TargetLevel), Application.loadedLevel))
            {
                if (menuType == MenuType.Main)
                {
                    menuType = MenuType.None;
                }

                return;
            }

            if (menuType == MenuType.Main)
            {
                if (winRect.Contains(Input.mousePosition))
                {
                    GameMain.Instance.MainCamera.SetControl(false);
                }
                else
                {
                    GameMain.Instance.MainCamera.SetControl(true);
                }
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                if (menuType == MenuType.None)
                {
                    menuType = MenuType.Main;
                }
                else
                {
                    menuType = MenuType.None;
                }
            }

            if (screenCreated)
            {
                maid = GameMain.Instance.CharacterMgr.GetMaid(0);
                if (maid == null)
                    return;
            }
        }

        public void OnGUI()
        {
            if (!Enum.IsDefined(typeof(TargetLevel), Application.loadedLevel))
            {
                return;
            }

            if (menuType == MenuType.None)
                return;

            maid = GameMain.Instance.CharacterMgr.GetMaid(0);
            if (maid == null)
                return;

            GUIStyle winStyle = "box";
            winStyle.fontSize = pv.Font("C1");
            winStyle.alignment = TextAnchor.UpperRight;

            if (lastScreenSize != new Vector2(Screen.width, Screen.height))
            {
                winRect = pv.PropScreenMH(winRect.x, winRect.y, guiWidth, 1f, lastScreenSize);
                lastScreenSize = new Vector2(Screen.width, Screen.height);
            }

            switch (menuType)
            {
                case MenuType.Main:
                    winRect = GUI.Window(0, winRect, DoMainMenu, "v0.0.0.1", winStyle);
                    break;
            }
        }
    }
}