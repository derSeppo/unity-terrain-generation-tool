using System.IO;
using UnityEditor;
using UnityEngine;

    public class TerrainGenerator : EditorWindow
    {
        private static readonly string[] HeightmapResolutionNames = {"33 x 33", "65 x 65", "129 x 129", "257 x 257", "513 x 513", "1025 x 1025", "2049 x 2049", "4097 x 4097"};
        private static readonly int[] HeightmapResolutions = {33, 65, 129, 257, 513, 1025, 2049, 4097};
        private static ComputeShader _shader;
        
        private int _width = 256;
        private int _length = 256;
        private int _maxHeight = 50;
        private int _heightmapResolution = 513;
        private bool _useBaseMap;
        private Texture2D _baseMap;

        private bool _useNoise = true;
        private float _scale = 20f;
        private Vector2 _generalOffset;
        private int _octaves = 5; //Amount of noise iterations
        private float _startingAmplitude = 1f; //Starting amplitude/strength of the noise
        private float _startingFrequency = 1f; //Starting frequency/scale of the noise
        private float _persistence = 0.5f; //Persistence rate of the noise amplitude
        private float _lacunarity = 2f; //Lacunarity of the noise frequency
        private float _flatness = 1.0f;
    
        private bool _useRandomOffset = true;
        private int _seed = 1234;
        private int _offsetMin = -1000;
        private int _offsetMax = 1000;
        
        private bool _useErosion = true;
        private int _iterations = 50;
        private float _rain = 0.01f;
        private float _solubility = 0.10f;
        private float _evaporation = 0.5f;

        private TerrainData _data;
        private GameObject _terrainGameObject;

        private Texture2D _heightmap;
        private bool _hasGenerated;
        private Vector2 _scrollPosition;

        [MenuItem("Tools/Terrain Generator")]
        private static void Init()
        {
            TerrainGenerator window = GetWindow<TerrainGenerator>(typeof(TerrainGenerator));
            window.Show();
        }

        private void OnGUI()
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
        
            Header("Terrain Settings");
            _width = EditorGUILayout.IntField("Width", _width);
            _length = EditorGUILayout.IntField("Length", _length);
            _maxHeight = EditorGUILayout.IntField("Maximum Height", _maxHeight);
            _heightmapResolution = EditorGUILayout.IntPopup("Heightmap Resolution", _heightmapResolution,
                HeightmapResolutionNames, HeightmapResolutions);

            _useBaseMap = EditorGUILayout.Toggle("Use Base Map", _useBaseMap);
            if (_useBaseMap)
            {
                _baseMap = (Texture2D) EditorGUILayout.ObjectField(
                    "Base Map",
                    _baseMap,
                    typeof(Texture2D),
                    false);
            }

            Header("Noise Settings");
            
            _useNoise = EditorGUILayout.Toggle("Apply Random Noise", _useNoise);
            if (_useNoise)
            {
                _generalOffset = EditorGUILayout.Vector2Field("Custom Offset", _generalOffset);
                _scale = EditorGUILayout.FloatField("Scale", _scale);
                _flatness = EditorGUILayout.FloatField("Flatness", _flatness);

                MiniHeader("Fractal Noise Settings");
                _octaves = EditorGUILayout.IntField("Octaves", _octaves);
                _startingAmplitude = EditorGUILayout.Slider("Starting Amplitude", _startingAmplitude, 0, 1);
                _persistence = EditorGUILayout.Slider("Persistence", _persistence, 0, 1);
                _startingFrequency = EditorGUILayout.FloatField("Starting Frequency", _startingFrequency);
                _lacunarity = EditorGUILayout.FloatField("Lacunarity", _lacunarity);


                MiniHeader("Random Octave Offset Settings");
                _useRandomOffset = EditorGUILayout.Toggle("Use Random Offset", _useRandomOffset);
                if (_useRandomOffset)
                {
                    _seed = EditorGUILayout.IntField("Random Generator Seed", _seed);
                    _offsetMin = EditorGUILayout.IntField("Offset Minimum", _offsetMin);
                    _offsetMax = EditorGUILayout.IntField("Offset Maximum", _offsetMax);
                }
            }
            
            Header("Erosion Settings");
            _useErosion = EditorGUILayout.Toggle("Apply Erosion", _useErosion);
            if (_useErosion)
            {
                _iterations = EditorGUILayout.IntField("Iterations", _iterations);
                _rain = EditorGUILayout.Slider("Rain Per Iteration", _rain, 0, 1);
                _solubility = EditorGUILayout.Slider("Solubility", _solubility, 0, 1);
                _evaporation = EditorGUILayout.Slider("Evaporation", _evaporation, 0, 1);
            }

            if (GUILayout.Button("Generate"))
            {
                _heightmap = StartGeneration();
            }

            if (_heightmap && _terrainGameObject)
            {
                if (GUILayout.Button("Export as PNG"))
                {
                    string path = EditorUtility.SaveFilePanel("Export heightmap as PNG", "", "heightmap", "png");
                
                    if (path.Length != 0)
                    {
                        byte[] pngBytes = _heightmap.EncodeToPNG();
                        if (pngBytes != null)
                            File.WriteAllBytes(path, pngBytes);
                    }
                }
            
                Header("Result");
                EditorGUILayout.LabelField(new GUIContent(_heightmap), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true), GUILayout.MinHeight(position.width), GUILayout.MaxWidth(_heightmapResolution),GUILayout.MaxHeight(_heightmapResolution));
            }
        
            GUILayout.EndScrollView();
        }

        private static void Header(string label)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
        }
        
        private static void MiniHeader(string label)
        {
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(3);
        }

        private Texture2D StartGeneration()
        {
            float[,] heightValues;
            if (_useNoise)
            {
                heightValues = GenerateHeights();
            }else if (_useBaseMap && _baseMap)
            {
                heightValues = GetHeightMapValues();
            }
            else
                return null;

            if (_useErosion)
                heightValues = GenerateErosion(heightValues);

            _data = new TerrainData();
            _data.heightmapResolution = _heightmapResolution;
            _data.size = new Vector3(_width, _maxHeight, _length);
            _data.SetHeights(0,0,heightValues);

            if(_terrainGameObject)
                DestroyImmediate(_terrainGameObject);
        
            _terrainGameObject = Terrain.CreateTerrainGameObject(_data);

            return GenerateTexture(heightValues);
        }

        private float[,] GetHeightMapValues()
        {
            float[,] heights = new float[_heightmapResolution, _heightmapResolution];
            for (int x = 0; x < _heightmapResolution; x++)
            {
                for (int y = 0; y < _heightmapResolution; y++)
                {
                    heights[x,y] = _baseMap.GetPixel(x, y).grayscale;
                }
            }

            return heights;
        }

        private float[,] GenerateHeights()
        {
            float[,] heights = new float[_heightmapResolution, _heightmapResolution];

            float minNoiseHeight = float.MaxValue;
            float maxNoiseHeight = float.MinValue;

            //Generate random x and y offsets for each octave
            Vector2[] offsets = new Vector2[_octaves];
            if (_useRandomOffset)
            {
                System.Random numberGenerator = new System.Random(_seed);
                for (int i = 0; i < _octaves; i++)
                {
                    offsets[i] = new Vector2(numberGenerator.Next(_offsetMin, _offsetMax), numberGenerator.Next(_offsetMin, _offsetMax));
                }
            }

            for (int x = 0; x < _heightmapResolution; x++)
            {
                for (int y = 0; y < _heightmapResolution; y++)
                {

                    float amplitude = _startingAmplitude;
                    float frequency = _startingFrequency;
                
                    for (int i = 0; i < _octaves; i++)
                    {
                        Vector2 noiseCoords = _generalOffset + offsets[i] + new Vector2(x/(float)_heightmapResolution, y/(float)_heightmapResolution) * (_scale * frequency);
                
                        heights[x, y] += Mathf.PerlinNoise(noiseCoords.x,noiseCoords.y) * amplitude;

                        amplitude *= _persistence;
                        frequency *= _lacunarity;
                    }

                    maxNoiseHeight = Mathf.Max(maxNoiseHeight, heights[x, y]);
                    minNoiseHeight = Mathf.Min(minNoiseHeight, heights[x, y]);
                }
            }
        
        
            for (int x = 0; x < _heightmapResolution; x++)
            {
                for (int y = 0; y < _heightmapResolution; y++)
                {

                    //Normalize values (Accumulated height value can be > 1, because of the octaves)
                    if(maxNoiseHeight - minNoiseHeight > 0)
                        heights[x, y] = (heights[x, y] - minNoiseHeight) / (maxNoiseHeight - minNoiseHeight);

                    //Redistribute values
                    heights[x, y] = Mathf.Pow(heights[x, y], _flatness);
                
                    //Fit values to the base map layout
                    if (_baseMap && _useBaseMap)
                    {
                        float heightLimit = _baseMap.GetPixel(x, y).grayscale;

                        heights[x, y] = heights[x, y] / 2 + heightLimit / 2;
                    }
                }
            }

            if(_useErosion)
                heights = GenerateErosion(heights);

            return heights;
        }
    
        private struct TerrainPoint
        {
            public float Height;
            public float Water;
        }
    
        private float[,] GenerateErosion(float[,] heights)
        {
            if (!_shader)
                _shader = Resources.Load<ComputeShader>("ErosionShader");

            int oneDimRes = _heightmapResolution * _heightmapResolution;
            int threadMod = _heightmapResolution % 16;

            TerrainPoint[] points = new TerrainPoint[oneDimRes];

            for (int x = 0; x < _heightmapResolution; x++)
            {
                for (int y = 0; y < _heightmapResolution; y++)
                {
                    points[x * _heightmapResolution + y] = new TerrainPoint
                    {
                        Height = heights[x, y],
                        Water = 0.0f
                    };
                }
            }
    
            int rainKernel = _shader.FindKernel("Rain");
            int erosionKernel = _shader.FindKernel("Erosion");
            int evaporationKernel = _shader.FindKernel("Evaporation");
    
            _shader.SetInt("resolution", _heightmapResolution);
            _shader.SetFloat("rain", _rain);
            _shader.SetFloat("solubility", _solubility);
            _shader.SetFloat("evaporation", _evaporation);
    
            ComputeBuffer rainBuffer = new ComputeBuffer(oneDimRes, 4 + 4);
            ComputeBuffer erosionBuffer = new ComputeBuffer(oneDimRes, 4 + 4);
            ComputeBuffer evaporationBuffer = new ComputeBuffer(oneDimRes, 4 + 4);
    
            evaporationBuffer.SetData(points);

            for (int c = 0; c < _iterations; c++)
            {

                _shader.SetBuffer(rainKernel, "inData", evaporationBuffer);
                _shader.SetBuffer(rainKernel, "outData", rainBuffer);

                _shader.Dispatch(rainKernel, _heightmapResolution / 16 + threadMod, _heightmapResolution / 16 + threadMod, 1);

                rainBuffer.GetData(points);
                erosionBuffer.SetData(points);

                _shader.SetBuffer(erosionKernel, "inData", rainBuffer);
                _shader.SetBuffer(erosionKernel, "outData", erosionBuffer);

                _shader.Dispatch(erosionKernel, _heightmapResolution / 16 + threadMod, _heightmapResolution / 16 + threadMod, 1);

                _shader.SetBuffer(evaporationKernel, "inData", erosionBuffer);
                _shader.SetBuffer(evaporationKernel, "outData", evaporationBuffer);

                _shader.Dispatch(evaporationKernel, _heightmapResolution / 16 + threadMod, _heightmapResolution / 16 + threadMod, 1);
            }

            evaporationBuffer.GetData(points);
        
            for (int x = 0; x < _heightmapResolution; x++)
            {
                for (int y = 0; y < _heightmapResolution; y++)
                {
                    heights[x, y] = points[x * _heightmapResolution + y].Height;
                    heights[x, y] += points[x * _heightmapResolution + y].Water * _solubility;
                }
            }

            rainBuffer.Dispose();
            erosionBuffer.Dispose();
            evaporationBuffer.Dispose();
            return heights;
        }

        private Texture2D GenerateTexture(float[,] heights)
        {
            Texture2D texture = new Texture2D(_heightmapResolution, _heightmapResolution);
        
            for (int i = 0; i < _heightmapResolution; i++)
            {
                for (int j = 0; j < _heightmapResolution; j++)
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
