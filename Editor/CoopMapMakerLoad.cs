#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Color = UnityEngine.Color;

public class CoopMapMakerLoad : ScriptableWizard
{
    /*public Material material;
    public MeshFilter meshFilter;*/

    public Shader defaultShader;
    [TextArea(100,200)]
    public string jsonData;

    [Serializable]
    public struct SpawnInfo
    {
        public int itemID;
        public float[] position;
        public float[] rotation;
        public float[] scale;
    }

    [Serializable]
    public struct ItemInfo
    {
        public int itemID;
        public string type;
        public string itemName;
        public string modelURL;
        public string textureURL;
        public string shaderName;
    }

    [Serializable]
    struct VoyageKensetu
    {
        public string type;
        public double version;
        public ItemInfo[] items;
        public SpawnInfo[] spawns;
    }

    [MenuItem("Voyage/Recreate Scene")]
    static void CreateWizard()
    {
        ScriptableWizard.DisplayWizard<CoopMapMakerLoad>("Download texture and generate mesh", "Create");
        //If you don't want to use the secondary button simply leave it out:
        //ScriptableWizard.DisplayWizard<WizardCreateLight>("Create Light", "Create");
    }


    Mesh generatedMesh;
    const int currentVersion = 2;
    const int metadataVersion = 4;
    const int metadataVertices = 8;
    const int metadataNormals = 9;
    const int metadataUvs = 10;
    const int metadataIndices = 11;
    const int metadataSizeInFloat = 64;
    const int float_size = 4;
    const int int_size = 4;
    const int nFloatsInColor = 4;
    const float supportedVersionMax = 2;

    const float VOY = 0x00564f59;
    const float AGE = 0x00454741;

    public const int minimumDataSize =
        metadataSizeInFloat
        + 3  // vertices
        + 3  // normals
        + 2  // uvs
        + 3; // indices

    void DebugLog(string message)
    {
        Debug.Log(message);
    }

    void ShowError(string errorMessage)
    {
        Debug.LogError(errorMessage);
    }

    bool MeshFromColors(Color[] colors, Mesh mesh)
    {
        if (colors == null)
        {
            DebugLog("No color data...");
            return false;
        }

        if (colors.Length <= minimumDataSize)
        {
            DebugLog($"Not enough data ({colors.Length} bytes)");
            return false;
        }

        if ((colors[0].r != VOY) | (colors[0].g != AGE))
        {
            Debug.Log($"{colors[0].r} != {VOY} ? {colors[0].r != VOY}");
            Debug.Log($"{colors[0].g} != {AGE} ? {colors[0].g != AGE}");
            ShowError("Not an encoded model. Invalid metadata");
            return false;
        }

        var currentVersion = colors[metadataVersion / nFloatsInColor].r;
        if (currentVersion > supportedVersionMax)
        {
            Debug.Log("Unsupported format");
            return false;
        }
        var infoCol = colors[2];
        int nVertices = (int)infoCol.r;
        int nNormals = (int)infoCol.g;
        int nUVS = (int)infoCol.b;
        int nIndices = (int)infoCol.a;

        Vector3[] vertices = new Vector3[nVertices];
        Vector3[] normals = new Vector3[nNormals];
        Vector2[] uvs = new Vector2[nUVS];
        int[] indices = new int[nIndices];

        int start = metadataSizeInFloat / nFloatsInColor;
        int cursor = start;
        for (int v = 0; v < nVertices; v++, cursor++)
        {
            Color currentCol = colors[cursor];
            Vector3 vertex = new Vector3(
                currentCol.r,
                currentCol.g,
                currentCol.b);
            vertices[v] = vertex;
        }

        for (int n = 0; n < nNormals; n++, cursor++)
        {
            Color currentCol = colors[cursor];
            Vector3 normal = new Vector3(
                currentCol.r,
                currentCol.g,
                currentCol.b);
            normals[n] = normal;
        }

        for (int u = 0; u < nUVS; u++)
        {
            Color currentCol = colors[cursor];
            Vector2 uv = new Vector2(
                currentCol.r,
                currentCol.g);
            uvs[u] = uv;
            cursor++;
        }

        int alignedIndices = nIndices / 4 * 4;
        int currentIndex = 0;
        for (; currentIndex < alignedIndices; currentIndex += 4, cursor++)
        {

            Color currentCol = colors[cursor];
            indices[currentIndex + 0] = (int)currentCol.r;
            indices[currentIndex + 1] = (int)currentCol.g;
            indices[currentIndex + 2] = (int)currentCol.b;
            indices[currentIndex + 3] = (int)currentCol.a;
        }

        int remainingIndices = nIndices - alignedIndices;
        if (remainingIndices > 0)
        {
            Color currentCol = colors[cursor];
            if (remainingIndices >= 1)
            {
                indices[currentIndex] = (int)currentCol.r;
                currentIndex++;
            }
            if (remainingIndices >= 2)
            {
                indices[currentIndex] = (int)currentCol.g;
                currentIndex++;
            }
            if (remainingIndices == 3)
            {
                indices[currentIndex] = (int)currentCol.b;
                currentIndex++;
            }
        }


        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = indices;

        return true;
    }


    void RefreshAssetsDatabase()
    {
        var refreshSettings = ImportAssetOptions.Default | ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(refreshSettings);
        
    }

    bool DownloadedEXRToMesh(string imageFilePath, string meshSavePath)
    {
        Debug.Log($"Getting texture at {imageFilePath}");

        Thread.Sleep(1000);
        TextureImporter textureImporter = (TextureImporter)TextureImporter.GetAtPath(imageFilePath);
        if (textureImporter == null)
        {
            Debug.LogError("Could not read texture importer settings. Bailing out");
            return false;
        }

        textureImporter.isReadable = true;
        textureImporter.filterMode = FilterMode.Point;
        textureImporter.wrapModeU = TextureWrapMode.Clamp;
        textureImporter.wrapModeV = TextureWrapMode.Clamp;
        textureImporter.wrapModeW = TextureWrapMode.Clamp;
        textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
        /* Such a pain to just say "I want a non filtered RGBA float texture out of
         * an EXR file...
         */
        var textureSettings = textureImporter.GetPlatformTextureSettings("Standalone");
        textureSettings.format = TextureImporterFormat.RGBAFloat;
        textureSettings.overridden = true;
        textureSettings.compressionQuality = 0;
        textureSettings.crunchedCompression = false;
        textureSettings.textureCompression = TextureImporterCompression.Uncompressed;
        textureImporter.SetPlatformTextureSettings(textureSettings);
        textureImporter.SaveAndReimport();
        Texture2D loadedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(imageFilePath);
        if (loadedTexture == null)
        {
            Debug.LogError("Could not load the texture back !");
            return false;
        }

        Mesh generatedMesh = new Mesh();
        bool conversionSuccessful = MeshFromColors(loadedTexture.GetPixels(), generatedMesh);
        if (conversionSuccessful)
        {
            AssetDatabase.CreateAsset(generatedMesh, meshSavePath);
        }

        RefreshAssetsDatabase();

        return conversionSuccessful;
    }





    void InitializeDownloadsState()
    {
        downloadsSuccessful = true;
    }

    string ItemsFolder()
    {
        return "Assets/items";
    }

    string ItemFolderGet(int itemID)
    {
        return "Assets/items/" + itemID;
    }

    string ItemFolderCreate(int itemID)
    {
        string folderPath = ItemFolderGet(itemID);
        if (AssetDatabase.IsValidFolder(folderPath)) return folderPath;

        AssetDatabase.CreateFolder("Assets/items", itemID.ToString());
        RefreshAssetsDatabase();
        return folderPath;
    }

    bool PrepareModelFolders(ItemInfo[] items)
    {
        if (!AssetDatabase.IsValidFolder(ItemsFolder()))
        {
            AssetDatabase.CreateFolder("Assets", "items");
            RefreshAssetsDatabase();
        }
        if (!AssetDatabase.IsValidFolder(ItemsFolder()))
        {
            return false;
        }

        foreach (var item in items)
        {
            var itemFolder = ItemFolderCreate(item.itemID);

            if (!AssetDatabase.IsValidFolder(itemFolder))
            {
                return false;
            }
        }

        return true;
    }

    bool downloadsSuccessful = false;
    string downloadError = "";

    IEnumerator DownloadImage(string mediaUrl, string projectRelativeFilePath)
    {

        string assetRelativeFilePath = projectRelativeFilePath.Replace("Assets", "");
        string savedFilePath = Application.dataPath + assetRelativeFilePath;

        Debug.Log($"Downloading {mediaUrl} to {savedFilePath}");

        UnityWebRequest request = new UnityWebRequest(
            mediaUrl,
            "GET",
            new DownloadHandlerFile(savedFilePath, false),
            null);
        yield return request.SendWebRequest();

        downloadsSuccessful &= !(request.isNetworkError | request.isHttpError);

        if (!downloadsSuccessful)
        {
            downloadError = $"When downloading {request.url} to {savedFilePath}, the following error occured : {request.error}";
        }

        Debug.Log($"Request for {request.url} is done ? {request.isDone}");

    }

    IEnumerator DownloadFiles(params (string url, string saveFilePath)[] filesToDownload)
    {
        foreach (var (url, savePath) in filesToDownload)
        {

            var stupidMecanism = DownloadImage(url, savePath);
            while (stupidMecanism.MoveNext()) yield return true;
            if (!downloadsSuccessful) break;
        }

    }

    IEnumerator DownloadModel(string itemFolder, string modelUrl, string textureUrl)
    {

        var encodedMeshSavePath = $"{itemFolder}/model.exr";
        var associatedTextureSavePath = $"{itemFolder}/texture1.png";

        Debug.Log("Before DownloadFiles");
        var downloader = DownloadFiles(
            (modelUrl, encodedMeshSavePath),
            (textureUrl, associatedTextureSavePath));
        while (downloader.MoveNext()) yield return true;
    }

    IEnumerator DownloadRegenerateAndSpawn(VoyageKensetu saveData)
    {
        IEnumerator enumerator;
        InitializeDownloadsState();
        foreach (var item in saveData.items)
        {

            enumerator = DownloadModel(ItemFolderGet(item.itemID), item.modelURL, item.textureURL);
            while (enumerator.MoveNext()) yield return true;
            if (!downloadsSuccessful)
            {
                Debug.LogError(downloadError);
                yield break;
            }
        }

        Dictionary<int, GameObject> generatedItems = new Dictionary<int, GameObject>(saveData.items.Length);
        if (downloadsSuccessful)
        {
            GenerateModels(saveData.items, generatedItems);
        }

        SpawnItems(saveData.spawns, generatedItems);
    }

    GameObject GenerateModelVoyageEXR(ItemInfo item)
    {

        string itemFolder = ItemFolderGet(item.itemID);
        var associatedTextureSavePath = $"{itemFolder}/texture1.png";
        var encodedMeshSavePath = $"{itemFolder}/model.exr";
        var decodedMeshSavePath = $"{itemFolder}/model.mesh";
        var generatedMaterialSavePath = $"{itemFolder}/material.mat";
        var prefabSavePath = $"{itemFolder}/item.prefab";
        
        Debug.Log("After DownloadFiles");

        AssetDatabase.ImportAsset(associatedTextureSavePath);
        AssetDatabase.ImportAsset(encodedMeshSavePath);
        RefreshAssetsDatabase();

        DownloadedEXRToMesh(encodedMeshSavePath, decodedMeshSavePath);
        AssetDatabase.ImportAsset(decodedMeshSavePath);

        RefreshAssetsDatabase();

        var generatedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(decodedMeshSavePath);
        if (generatedMesh == null)
        {
            Debug.LogError($"Could not load mesh at {decodedMeshSavePath}");
            return null;
        }

        var associatedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(associatedTextureSavePath);
        var shader = Shader.Find(item.shaderName);
        if (shader == null) shader = defaultShader;
        var material = new Material(shader);
        if (associatedTexture != null) material.SetTexture("_MainTex", associatedTexture);

        AssetDatabase.CreateAsset(material, generatedMaterialSavePath);

        GameObject gameObject = new GameObject($"Item-{item.itemID}");

        gameObject.transform.localRotation = Quaternion.Euler(new Vector3(-90, 0, 0));

        var itemMeshFilter = gameObject.AddComponent<MeshFilter>();
        var itemMeshRenderer = gameObject.AddComponent<MeshRenderer>();
        itemMeshFilter.sharedMesh = generatedMesh;
        itemMeshRenderer.sharedMaterial = material;

        var generatedPrefab = PrefabUtility.SaveAsPrefabAsset(gameObject, prefabSavePath);
        if (generatedPrefab == null)
        {
            Debug.LogError("Could not create prefab...");
            return generatedPrefab;
        }
        RefreshAssetsDatabase();
        DestroyImmediate(gameObject);

        var labels = new List<string>(AssetDatabase.GetLabels(generatedPrefab))
        {
            "Voyage-Kensetsu",
            item.itemName
        };
        AssetDatabase.SetLabels(generatedPrefab, labels.ToArray());
        RefreshAssetsDatabase();

        return generatedPrefab;
    }

    Vector3 VectorFromArray(float[] values)
    {
        return new Vector3(values[0], values[1], values[2]);
    }

    Quaternion QuaternionFromArray(float[] values)
    {
        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    void SpawnItems(SpawnInfo[] spawns, Dictionary <int,GameObject> itemsPrefab)
    {
        foreach (var spawnInfo in spawns)
        {
            var itemPrefab = itemsPrefab[spawnInfo.itemID];
            if (itemPrefab == null)
            {
                Debug.LogWarning($"Unknown reference {spawnInfo.itemID}");
                continue;
            }

            var instance = (GameObject) PrefabUtility.InstantiatePrefab(itemPrefab);
            Debug.Log(spawnInfo.position);
            Debug.Log(spawnInfo.rotation);
            instance.transform.SetPositionAndRotation(
                VectorFromArray(spawnInfo.position),
                QuaternionFromArray(spawnInfo.rotation));
            instance.transform.localScale = VectorFromArray(spawnInfo.scale);
        }
    }

    void GenerateModels(ItemInfo[] items, Dictionary<int,GameObject> itemsPrefab)
    {
        RefreshAssetsDatabase();

        foreach (var item in items)
        {
            var prefab = GenerateModelVoyageEXR(item);
            if (prefab != null)
            {
                itemsPrefab[item.itemID] = prefab;
            }
        }
    }



    void OnWizardCreate()
    {
        //EditorApplication.update = ProcessFiles;
        InitializeDownloadsState();

        /*string modelUrl = "https://cdn.discordapp.com/attachments/1078525121972686993/1112458649193959524/Wesai-pokemon-red.exr";
        string textureUrl = "https://cdn.discordapp.com/attachments/1078525121972686993/1112457515540693012/fireRed_diffuse.png";

        ItemInfo[] items = new ItemInfo[]
        {
            new ItemInfo()
            {
                itemID = 1,
                itemName = "Pokemon Room (Red)",
                modelURL = modelUrl,
                textureURL = textureUrl,
                shaderName = "Unlit/Texture"
            },
            new ItemInfo()
            {
                itemID = 2,
                itemName = "Cake",
                modelURL = "https://cdn.discordapp.com/attachments/1078525121972686993/1112441625872973904/Anna-Espenstein-Cake.exr",
                textureURL = "https://cdn.discordapp.com/attachments/1078525121972686993/1112438785544822935/resized_cake_texture.png",
                shaderName = "Standard"
            },
            new ItemInfo()
            {
                itemID = 2048,
                itemName = "Degu",
                modelURL = "https://cdn.discordapp.com/attachments/1078525121972686993/1110767030182887444/Degu-Casquette.exr",
                textureURL = "https://cdn.discordapp.com/attachments/1078525121972686993/1110498084661772318/AtlasCasquette_2K.png",
                shaderName = "Unlit/Texture"
            }
        };*/

        VoyageKensetu saveData = JsonUtility.FromJson<VoyageKensetu>(jsonData);

        if (!PrepareModelFolders(saveData.items)) return;
        if (defaultShader == null) defaultShader = Shader.Find("Standard");



        Myy.EditorCoroutine.Start(DownloadRegenerateAndSpawn(saveData));
    }


}

namespace Myy
{

    public class EditorCoroutine
    {
        public static EditorCoroutine Start(IEnumerator _routine)
        {
            EditorCoroutine coroutine = new EditorCoroutine(_routine);
            coroutine.Start();
            return coroutine;
        }

        readonly IEnumerator routine;
        EditorCoroutine(IEnumerator _routine)
        {
            routine = _routine;
        }

        void Start()
        {
            EditorApplication.update += Update;
        }
        public void Stop()
        {
            EditorApplication.update -= Update;
        }

        void Update()
        {
            /* NOTE: no need to try/catch MoveNext,
             * if an IEnumerator throws its next iteration returns false.
             * Also, Unity probably catches when calling EditorApplication.update.
             */

            if (!routine.MoveNext())
            {
                Stop();
            }
        }
    }
}
#endif