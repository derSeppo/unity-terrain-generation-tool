using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public class TerrainGenerator : EditorWindow
{
    public Texture2D baseMap;
    
    [Min(1)]
    public int width = 256;
    [Min(1)]
    public int length = 256;
    [Min(0)]
    public int maxHeight = 50;
    
    public int heightmapResolution = 513;
    
    [Min(0)]
    public float xOffset = 0;
    [Min(0)]
    public float yOffset = 0;
    
    [Header("Detail Noise")]
    [Min(0)]//, Description("Amount of noise iterations")]
    public int octaves = 5;

    [Range(0,1)]//, Description("Starting amplitude/strength of the noise")]
    public float startingAmplitude = 1f;
    
    [Min(1)]//, Description("Starting frequency/scale of the noise")]
    public float startingFrequency = 1f;
    
    [Range(0,1)]//, Description("Persistence rate of the noise amplitude")]
    public float persistence = 0.5f;

    [Min(1)]//, Description("Lacunarity of the noise frequency")]
    public float lacunarity = 2f;
    
    [Min(0.0001f)]
    public float scale = 20f;

    [Min(1.0f)] 
    public float flatness = 1.0f;
    
    public int seed = 1234;
    
    public int offsetMin = -1000;
    
    public int offsetMax = 1000;
    
    [Header("Erosion")]
    public bool applyErosion = true;
    [Min(1)] 
    public int iterations = 50;
    public float rain = 0.01f;
    public float solubility = 0.10f;
    public float evaporation = 0.5f;
    
    [Header("Options")]
    public bool useBaseMap = false;
    
    private static readonly string[] heightmapResolutionNames = new string[]{"33 x 33", "65 x 65", "129 x 129", "257 x 257", "513 x 513", "1025 x 1025", "2049 x 2049", "4097 x 4097"};
    private static readonly int[] heightmapResolutions = {33, 65, 129, 257, 513, 1025, 2049, 4097};
    private static ComputeShader shader;
    
    private TerrainData data;
    private GameObject terrainGameObject;

    private Texture2D heightmap;
    private bool generated = false;
    private Vector2 scrollPosition;

    [MenuItem("Tools/Terrain Generator")]
    public static void Init()
    {
        shader = Resources.Load<ComputeShader>("ErosionShader");

        TerrainGenerator window = (TerrainGenerator)GetWindow(typeof(TerrainGenerator));
        window.Show();
    }

    private void OnGUI()
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        
        Header("Terrain Settings");
        width = EditorGUILayout.IntField("Width", width);
        length = EditorGUILayout.IntField("Length", length);
        maxHeight = EditorGUILayout.IntField("MaxHeight", maxHeight);
        heightmapResolution = EditorGUILayout.IntPopup("Heightmap Resolution", heightmapResolution,
            heightmapResolutionNames, heightmapResolutions);

        useBaseMap = EditorGUILayout.Toggle("Use Base Map", useBaseMap);
        if (useBaseMap)
        {
            baseMap = (Texture2D) EditorGUILayout.ObjectField(
                "Base Map",
                baseMap,
                typeof(Texture2D),
                false);
        }

        Header("General Noise Settings");
        xOffset = EditorGUILayout.FloatField("X Offset", xOffset);
        yOffset = EditorGUILayout.FloatField("Y Offset", yOffset);
        scale = EditorGUILayout.FloatField("Scale", scale);

        Header("Detail Noise Settings");
        octaves = EditorGUILayout.IntField("Octaves", octaves);
        startingAmplitude = EditorGUILayout.FloatField("Starting Amplitude", startingAmplitude);
        persistence = EditorGUILayout.FloatField("Persistence", persistence);
        startingFrequency = EditorGUILayout.FloatField("Starting Frequency", startingFrequency);
        lacunarity = EditorGUILayout.FloatField("Lacunarity", lacunarity);
        flatness = EditorGUILayout.FloatField("Flatness", flatness);
        
        Header("Random Octave Offset Settings");
        seed = EditorGUILayout.IntField("Random Generator Seed", seed);
        offsetMin = EditorGUILayout.IntField("Offset Minimum", offsetMin);
        offsetMax = EditorGUILayout.IntField("Offset Maximum", offsetMax);
        
        Header("Erosion Settings");
        applyErosion = EditorGUILayout.Toggle("Apply Erosion", applyErosion);

        if (applyErosion)
        {
            iterations = EditorGUILayout.IntField("Iterations", iterations);
            rain = EditorGUILayout.FloatField("Rain Per Iteration", rain);
            solubility = EditorGUILayout.FloatField("Soluibilty", solubility);
            evaporation = EditorGUILayout.FloatField("Evaporation", evaporation);
        }

        if (GUILayout.Button("Generate"))
        {
            heightmap = StartGeneration();
            generated = true;
        }

        if (generated)
        {
            if (GUILayout.Button("Export as PNG"))
            {
                string path = EditorUtility.SaveFilePanel("Export heightmap as PNG", "", "heightmap", "png");
                
                if (path.Length != 0)
                {
                    byte[] pngBytes = heightmap.EncodeToPNG();
                    if (pngBytes != null)
                        File.WriteAllBytes(path, pngBytes);
                }
            }
            
            Header("Result");
            EditorGUILayout.LabelField(new GUIContent(heightmap), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true), GUILayout.MinHeight(position.width));
        }
        
        GUILayout.EndScrollView();
    }

    private static void Header(string label)
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUILayout.Space(5);
    }

    private Texture2D StartGeneration()
    {
        float[,] heightValues = GenerateHeights();
        
        data = new TerrainData();
        data.heightmapResolution = heightmapResolution;
        data.size = new Vector3(width, maxHeight, length);
        data.SetHeights(0,0,heightValues);

        if(terrainGameObject != null)
            DestroyImmediate(terrainGameObject);
        
        terrainGameObject = Terrain.CreateTerrainGameObject(data);

        return GenerateTexture(heightValues);
    }

    private float[,] GenerateHeights()
    {
        float[,] heights = new float[heightmapResolution, heightmapResolution];

        float minNoiseHeight = float.MaxValue;
        float maxNoiseHeight = float.MinValue;

        System.Random numberGenerator = new System.Random(seed);
        
        //Generate random x and y offsets for each octave
        Vector2[] offsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++) {
            offsets[i] = new Vector2 (numberGenerator.Next (offsetMin, offsetMax), numberGenerator.Next (offsetMin, offsetMax));
        }
        
        for (int x = 0; x < heightmapResolution; x++)
        {
            for (int y = 0; y < heightmapResolution; y++)
            {

                float amplitude = startingAmplitude;
                float frequency = startingFrequency;
                
                for (int i = 0; i < octaves; i++)
                {
                    Vector2 noiseCoords = offsets[i] + new Vector2(x / (float)heightmapResolution, y / (float)heightmapResolution) * (scale * frequency);
                
                    heights[x, y] += Mathf.PerlinNoise(noiseCoords.x,noiseCoords.y) * amplitude;

                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                maxNoiseHeight = Mathf.Max(maxNoiseHeight, heights[x, y]);
                minNoiseHeight = Mathf.Min(minNoiseHeight, heights[x, y]);
            }
        }
        
        
        for (int x = 0; x < heightmapResolution; x++)
        {
            for (int y = 0; y < heightmapResolution; y++)
            {

                //Normalize values (Accumulated height value can be > 1, because of the octaves)
                if(maxNoiseHeight - minNoiseHeight > 0)
                    heights[x, y] = (heights[x, y] - minNoiseHeight) / (maxNoiseHeight - minNoiseHeight);

                //Redistribute values
                heights[x, y] = Mathf.Pow(heights[x, y], flatness);
                
                //Fit values to the base map layout
                if (baseMap != null && useBaseMap == true)
                {
                    float heightLimit = baseMap.GetPixel(x, y).grayscale;

                    heights[x, y] = (heights[x, y] + 1) / 2 * heightLimit;
                }
            }
        }

        if(applyErosion)
            heights = GenerateErosion(heights);

        return heights;
    }
    
    private struct TerrainPoint
    {
        public float height;
        public float water;
    }
    
    private float[,] GenerateErosion(float[,] heights)
    {
        int oneDimRes = heightmapResolution * heightmapResolution;

        TerrainPoint[] points = new TerrainPoint[oneDimRes];
        
        float[][] waterMap = new float[heightmapResolution][];
        for (int index = 0; index < heightmapResolution; index++)
        {
            waterMap[index] = new float[heightmapResolution];
        }
        
        for (int x = 0; x < heightmapResolution; x++)
        {
            for (int y = 0; y < heightmapResolution; y++)
            {
                points[x * heightmapResolution + y] = new TerrainPoint
                {
                    height = heights[x, y],
                    water = waterMap[x][y]
                };
            }
        }
    
        int rainKernel = shader.FindKernel("Rain");
        int erosionKernel = shader.FindKernel("Erosion");
        int evaporationKernel = shader.FindKernel("Evaporation");
    
        shader.SetInt("resolution", heightmapResolution);
        shader.SetFloat("rain", rain);
        shader.SetFloat("solubility", solubility);
        shader.SetFloat("evaporation", evaporation);
    
        ComputeBuffer rainBuffer = new ComputeBuffer(oneDimRes, 4 + 4);
        ComputeBuffer erosionBuffer = new ComputeBuffer(oneDimRes, 4 + 4);
        ComputeBuffer evaporationBuffer = new ComputeBuffer(oneDimRes, 4 + 4);
    
        evaporationBuffer.SetData(points);

        for (int c = 0; c < iterations; c++)
        {

            shader.SetBuffer(rainKernel, "inData", evaporationBuffer);
            shader.SetBuffer(rainKernel, "outData", rainBuffer);

            shader.Dispatch(rainKernel, heightmapResolution / 9, heightmapResolution / 9, 1);

            rainBuffer.GetData(points);
            erosionBuffer.SetData(points);

            shader.SetBuffer(erosionKernel, "inData", rainBuffer);
            shader.SetBuffer(erosionKernel, "outData", erosionBuffer);

            shader.Dispatch(erosionKernel, heightmapResolution / 9, heightmapResolution / 9, 1);

            shader.SetBuffer(evaporationKernel, "inData", erosionBuffer);
            shader.SetBuffer(evaporationKernel, "outData", evaporationBuffer);

            shader.Dispatch(evaporationKernel, heightmapResolution / 9, heightmapResolution / 9, 1);
        }

        evaporationBuffer.GetData(points);
        
        for (int x = 0; x < heightmapResolution; x++)
        {
            for (int y = 0; y < heightmapResolution; y++)
            {
                heights[x, y] = points[x * heightmapResolution + y].height;
                waterMap[x][y] = points[x * heightmapResolution + y].water;
            }
        }

        for (int i = 0; i < heightmapResolution; i++)
        {
            for (int j = 0; j < heightmapResolution; j++)
            {
                heights[i,j] += waterMap[i][j] * solubility;
            }
        }

        rainBuffer.Dispose();
        erosionBuffer.Dispose();
        evaporationBuffer.Dispose();
        return heights;
    }

    private Texture2D GenerateTexture(float[,] heights)
    {
        Texture2D texture = new Texture2D(heightmapResolution, heightmapResolution);
        
        for (int i = 0; i < heightmapResolution; i++)
        {
            for (int j = 0; j < heightmapResolution; j++)
            {
                float height = heights[i, j];
                
                Color color = new Color(height,height,height);
                
                texture.SetPixel(i,j, color);
            }
        }

        texture.Apply();
        return texture;
    }
}
