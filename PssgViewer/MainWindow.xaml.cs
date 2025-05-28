using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml;
using HelixToolkit.Wpf;
using Microsoft.Win32;
// Use aliases to differentiate between the ambiguous types
using WinVector = System.Windows.Media.Media3D.Vector3D;
using WinQuaternion = System.Windows.Media.Media3D.Quaternion;
using NumVector = System.Numerics.Vector3;

namespace PssgViewer
{
    /// <summary>
    /// Main window for the PSSG Viewer application
    /// </summary>
    public partial class MainWindow : Window
    {
        // Data storage
        private Dictionary<string, SceneNode> sceneNodes = new Dictionary<string, SceneNode>();
        private Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        private Dictionary<string, GeometryBlock> geometryBlocks = new Dictionary<string, GeometryBlock>();
        private Dictionary<string, string> renderSourceMap = new Dictionary<string, string>();
        private Dictionary<string, string> segmentMap = new Dictionary<string, string>();

        // Track current camera state to lock roll
        private WinVector cameraUpDirection = new WinVector(0, 0, 1);

        // Camera control
        private bool isRotating = false;
        private Point lastMousePosition;

        public MainWindow()
        {
            InitializeComponent();
        }

        #region File Operations

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PSSG Files (*.pssg;*.xml)|*.pssg;*.xml|All files (*.*)|*.*",
                Title = "Select a PSSG File"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string filePath = dialog.FileName;
                    txtFilePath.Text = filePath;
                    LoadPssgFile(filePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void LoadPssgFile(string filePath)
        {
            // Reset everything
            ClearAll();

            try
            {
                XmlDocument document = new XmlDocument();

                if (Path.GetExtension(filePath).Equals(".pssg", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus("Converting PSSG to XML...");
                    // Convert to XML file then load it. Conversion simply recreates
                    // the node hierarchy since attribute parsing is not implemented.
                    string xmlPath = ConvertPssgToXml(filePath);
                    document.Load(xmlPath);
                    txtFilePath.Text = xmlPath;
                }
                else
                {
                    // Load XML document directly
                    document.Load(filePath);
                }

                // Parse content in proper order
                ParseShaders(document);
                ParseGeometryBlocks(document);
                MapRenderSources(document);
                BuildSceneTree(document);

                // Update status
                UpdateStatus($"File loaded successfully. Found {sceneNodes.Count} nodes, {shaders.Count} shaders, {geometryBlocks.Count} geometry blocks.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error parsing file: {ex.Message}");
                MessageBox.Show($"Error parsing file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void BuildXmlFromPssgNode(PssgNode node, XmlElement parent, XmlDocument doc)
        {
            foreach (var child in node.Children)
            {
                XmlElement elem = doc.CreateElement(child.Name);
                parent.AppendChild(elem);
                BuildXmlFromPssgNode(child, elem, doc);
            }
        }

        private static string ConvertPssgToXml(string pssgPath)
        {
            var archive = PssgArchive.Load(pssgPath);
            XmlDocument doc = new XmlDocument();
            XmlDeclaration decl = doc.CreateXmlDeclaration("1.0", "utf-8", null);
            doc.AppendChild(decl);
            XmlElement root = doc.CreateElement(archive.Root.Name);
            doc.AppendChild(root);
            BuildXmlFromPssgNode(archive.Root, root, doc);
            string xmlPath = System.IO.Path.ChangeExtension(pssgPath, ".xml");
            // Use a temporary file if an XML with that name already exists
            if (File.Exists(xmlPath))
            {
                string tempName = Path.GetFileNameWithoutExtension(xmlPath) + "_converted.xml";
                xmlPath = Path.Combine(Path.GetDirectoryName(xmlPath) ?? string.Empty, tempName);
            }
            doc.Save(xmlPath);
            return xmlPath;
        }

        private void ClearAll()
        {
            // Clear data structures
            sceneNodes.Clear();
            shaders.Clear();
            geometryBlocks.Clear();
            renderSourceMap.Clear();
            segmentMap.Clear();

            // Reset UI
            treeView.Items.Clear();
            detailsTextBox.Text = "";
            modelContainer.Children.Clear();
            modelInfoText.Text = "";

            UpdateStatus("Ready");
        }

        #endregion

        #region XML Parsing

        private void ParseShaders(XmlDocument document)
        {
            XmlNodeList shaderNodes = document.SelectNodes("//SHADERINSTANCE");
            if (shaderNodes == null) return;

            foreach (XmlNode node in shaderNodes)
            {
                string id = node.Attributes?["id"]?.Value;
                if (string.IsNullOrEmpty(id)) continue;

                string shaderGroup = node.Attributes?["shaderGroup"]?.Value;
                Shader shader = new Shader
                {
                    Id = id,
                    ShaderGroup = shaderGroup
                };

                // Parse textures
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.Name == "SHADERINPUT" && child.Attributes?["type"]?.Value == "texture")
                    {
                        string texture = child.Attributes?["texture"]?.Value;
                        if (!string.IsNullOrEmpty(texture))
                            shader.Textures.Add(texture);
                    }
                }

                shaders[id] = shader;
            }
        }

        private void ParseGeometryBlocks(XmlDocument document)
        {
            XmlNodeList blockNodes = document.SelectNodes("//DATABLOCK");
            if (blockNodes == null) return;

            foreach (XmlNode node in blockNodes)
            {
                string id = node.Attributes?["id"]?.Value;
                if (string.IsNullOrEmpty(id)) continue;

                int elementCount = int.Parse(node.Attributes?["elementCount"]?.Value ?? "0");
                int streamCount = int.Parse(node.Attributes?["streamCount"]?.Value ?? "0");

                GeometryBlock block = new GeometryBlock
                {
                    Id = id,
                    ElementCount = elementCount,
                    StreamCount = streamCount,
                    XmlNode = node
                };

                // Parse streams
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.Name == "DATABLOCKSTREAM")
                    {
                        string renderType = child.Attributes?["renderType"]?.Value;
                        string dataType = child.Attributes?["dataType"]?.Value;
                        int offset = int.Parse(child.Attributes?["offset"]?.Value ?? "0");
                        int stride = int.Parse(child.Attributes?["stride"]?.Value ?? "0");

                        block.Streams.Add(new DataStream
                        {
                            RenderType = renderType,
                            DataType = dataType,
                            Offset = offset,
                            Stride = stride
                        });
                    }
                }

                geometryBlocks[id] = block;
            }
        }

        private void MapRenderSources(XmlDocument document)
        {
            // Map segment sets to render data sources
            XmlNodeList segmentSets = document.SelectNodes("//SEGMENTSET");
            if (segmentSets == null) return;

            foreach (XmlNode segmentSet in segmentSets)
            {
                string segmentId = segmentSet.Attributes?["id"]?.Value;
                if (string.IsNullOrEmpty(segmentId)) continue;

                // Find RENDERDATASOURCE in this segment
                XmlNodeList renderSources = segmentSet.SelectNodes(".//RENDERDATASOURCE");
                foreach (XmlNode renderSource in renderSources)
                {
                    string sourceId = renderSource.Attributes?["id"]?.Value;
                    if (string.IsNullOrEmpty(sourceId)) continue;

                    // Map render streams to geometry blocks
                    XmlNodeList streamNodes = renderSource.SelectNodes(".//RENDERSTREAM");
                    foreach (XmlNode streamNode in streamNodes)
                    {
                        string streamId = streamNode.Attributes?["id"]?.Value;
                        string blockId = streamNode.Attributes?["dataBlock"]?.Value;

                        if (!string.IsNullOrEmpty(streamId) && !string.IsNullOrEmpty(blockId))
                        {
                            // Remove the # from the block reference
                            blockId = blockId.TrimStart('#');
                            renderSourceMap[streamId] = blockId;
                        }
                    }

                    // Map sources to segment sets
                    segmentMap[sourceId] = segmentId;
                }
            }
        }

        #endregion

        #region Scene Tree Construction

        private void BuildSceneTree(XmlDocument document)
        {
            treeView.Items.Clear();

            // Add root "Objects" node
            TreeViewItem objectsNode = new TreeViewItem
            {
                Header = "Objects",
                IsExpanded = true,
                Tag = "objects"
            };
            treeView.Items.Add(objectsNode);

            // Find root nodes
            XmlNodeList rootNodes = document.SelectNodes("//ROOTNODE");
            if (rootNodes != null)
            {
                List<TreeViewItem> objectItems = new List<TreeViewItem>();

                foreach (XmlNode rootNode in rootNodes)
                {
                    string id = rootNode.Attributes?["id"]?.Value;
                    if (string.IsNullOrEmpty(id)) continue;

                    // Get object name
                    string objectName = id;
                    if (objectName.EndsWith(" Root"))
                        objectName = objectName.Substring(0, objectName.Length - 5);

                    // Create tree item
                    TreeViewItem objectItem = new TreeViewItem
                    {
                        Header = objectName,
                        Tag = new SceneNode { XmlNode = rootNode, Id = id, Type = "ROOTNODE" },
                        IsExpanded = false
                    };

                    // Process child nodes
                    ProcessNode(rootNode, objectItem);
                    objectItems.Add(objectItem);
                }

                // Sort by name and add to tree
                objectItems.Sort((a, b) => string.Compare(a.Header.ToString(), b.Header.ToString(), StringComparison.OrdinalIgnoreCase));
                foreach (var item in objectItems)
                    objectsNode.Items.Add(item);
            }
        }

        private void ProcessNode(XmlNode xmlNode, TreeViewItem parentItem)
        {
            // Process child nodes
            foreach (XmlNode child in xmlNode.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element &&
                    (child.Name == "NODE" || child.Name == "RENDERNODE" || child.Name == "LODVISIBLERENDERNODE"))
                {
                    string id = child.Attributes?["id"]?.Value;
                    string nickname = child.Attributes?["nickname"]?.Value;

                    if (string.IsNullOrEmpty(id)) continue;

                    string displayName = !string.IsNullOrEmpty(nickname) ? nickname : id;
                    bool isModelNode = (child.Name == "RENDERNODE" || child.Name == "LODVISIBLERENDERNODE");

                    // Create tree item
                    TreeViewItem nodeItem = new TreeViewItem
                    {
                        Header = displayName,
                        Tag = new SceneNode
                        {
                            XmlNode = child,
                            Id = id,
                            Type = child.Name,
                            IsModelNode = isModelNode
                        },
                        IsExpanded = false
                    };

                    parentItem.Items.Add(nodeItem);

                    // If this is a model node, add materials to it
                    if (isModelNode)
                    {
                        // Add materials node
                        TreeViewItem materialsItem = new TreeViewItem
                        {
                            Header = "Materials",
                            Tag = "materials",
                            IsExpanded = false
                        };
                        nodeItem.Items.Add(materialsItem);

                        // Add materials from this node
                        AddMaterialsToNode(child, materialsItem);

                        // If this is an LOD node, add additional LOD levels
                        if (child.Name == "LODVISIBLERENDERNODE")
                        {
                            XmlNode lodInstances = child.SelectSingleNode("./LODRENDERINSTANCES");
                            if (lodInstances != null)
                            {
                                XmlNodeList lodLevels = lodInstances.SelectNodes("./LODRENDERINSTANCELIST");
                                if (lodLevels != null && lodLevels.Count > 0)
                                {
                                    // Add each LOD level
                                    for (int i = 0; i < lodLevels.Count; i++)
                                    {
                                        XmlNode lodLevel = lodLevels[i];
                                        string lodValue = lodLevel.Attributes?["lod"]?.Value ?? $"{i}";

                                        TreeViewItem lodLevelItem = new TreeViewItem
                                        {
                                            Header = $"lod{i + 1} ({lodValue})",
                                            Tag = new SceneNode
                                            {
                                                XmlNode = lodLevel,
                                                Id = $"{id}_lod{i + 1}",
                                                Type = "LOD_LEVEL",
                                                LodValue = lodValue,
                                                IsModelNode = true
                                            },
                                            IsExpanded = false
                                        };
                                        nodeItem.Items.Add(lodLevelItem);

                                        // Add materials for this LOD level
                                        TreeViewItem lodMaterialsItem = new TreeViewItem
                                        {
                                            Header = "Materials",
                                            Tag = "materials",
                                            IsExpanded = false
                                        };
                                        lodLevelItem.Items.Add(lodMaterialsItem);

                                        // Add materials from this LOD level
                                        AddMaterialsToNode(lodLevel, lodMaterialsItem);
                                    }
                                }
                            }
                        }
                    }
                    else if (child.Name == "NODE")
                    {
                        // Process child nodes recursively
                        ProcessNode(child, nodeItem);
                    }
                }
            }
        }

        private void AddMaterialsToNode(XmlNode node, TreeViewItem materialsItem)
        {
            // Find render instances for this node
            XmlNodeList renderInstances = node.SelectNodes("./RENDERSTREAMINSTANCE");
            if (renderInstances == null || renderInstances.Count == 0) return;

            // Track materials we've already processed
            Dictionary<string, bool> processedMaterials = new Dictionary<string, bool>();

            // Process each render instance
            foreach (XmlNode instance in renderInstances)
            {
                string shaderId = instance.Attributes?["shader"]?.Value;
                if (string.IsNullOrEmpty(shaderId)) continue;

                shaderId = shaderId.TrimStart('#');

                // Skip duplicates
                if (processedMaterials.ContainsKey(shaderId))
                    continue;

                processedMaterials[shaderId] = true;

                // Find source reference
                XmlNode sourceRef = instance.SelectSingleNode("./RENDERINSTANCESOURCE");
                if (sourceRef == null) continue;

                string sourceId = sourceRef.Attributes?["source"]?.Value;
                if (string.IsNullOrEmpty(sourceId)) continue;

                sourceId = sourceId.TrimStart('#');

                // Find geometry block for this source
                string geometryId = FindGeometryForSource(sourceId);
                if (string.IsNullOrEmpty(geometryId)) continue;

                // Create material tree item
                TreeViewItem materialItem = new TreeViewItem
                {
                    Header = shaderId,
                    Tag = new Material
                    {
                        ShaderId = shaderId,
                        GeometryId = geometryId,
                        SourceId = sourceId,
                        Instance = instance,
                        ParentNode = node
                    },
                    IsExpanded = false
                };
                materialsItem.Items.Add(materialItem);

                // Add textures if available
                if (shaders.TryGetValue(shaderId, out Shader shader) && shader.Textures.Count > 0)
                {
                    TreeViewItem texturesItem = new TreeViewItem
                    {
                        Header = "Textures",
                        Tag = "textures",
                        IsExpanded = false
                    };
                    materialItem.Items.Add(texturesItem);

                    // Add each texture
                    foreach (string texture in shader.Textures)
                    {
                        texturesItem.Items.Add(new TreeViewItem
                        {
                            Header = texture,
                            Tag = texture
                        });
                    }
                }
            }
        }

        private string FindGeometryForSource(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return null;

            // Remove any leading # if present
            sourceId = sourceId.TrimStart('#');

            // First try for exact match
            if (renderSourceMap.TryGetValue(sourceId, out string value))
                return value;

            // Then try for keys that start with sourceId + "_"
            var possibleMatch = renderSourceMap
                .FirstOrDefault(entry => entry.Key.StartsWith(sourceId + "_"));
            if (!string.IsNullOrEmpty(possibleMatch.Key))
                return possibleMatch.Value;

            // Then try for keys where sourceId is a substring after splitting on _
            foreach (var entry in renderSourceMap)
            {
                string[] parts = entry.Key.Split('_');
                if (parts.Contains(sourceId))
                    return entry.Value;
            }

            // Not found using any method
            return null;
        }

        #endregion

        #region Selection and Display

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag != null)
                DisplaySelectedItem(item.Tag);
        }

        private void DisplaySelectedItem(object selectedItem)
        {
            // Determine if this is an item that should be displayed in 3D
            bool show3D = selectedItem is SceneNode node && node.IsModelNode ||
                          selectedItem is Material;

            // Switch view mode
            detailsTextBox.Visibility = show3D ? Visibility.Collapsed : Visibility.Visible;
            modelView.Visibility = show3D ? Visibility.Visible : Visibility.Collapsed;
            viewHelpPanel.Visibility = show3D ? Visibility.Visible : Visibility.Collapsed;
            modelInfoPanel.Visibility = show3D ? Visibility.Visible : Visibility.Collapsed;

            // Clear previous content
            if (show3D)
            {
                modelContainer.Children.Clear();
                modelInfoText.Text = "";
            }

            // Display appropriate content
            if (selectedItem is SceneNode sceneNode)
            {
                if (sceneNode.IsModelNode)
                    RenderNodeModel(sceneNode);
                else
                    ShowNodeDetails(sceneNode);
            }
            else if (selectedItem is string tagString)
            {
                ShowTagDetails(tagString);
            }
            else if (selectedItem is Material material)
            {
                RenderMaterialMesh(material);
            }
            else
            {
                detailsTextBox.Text = "Select an item to view details";
            }
        }

        private void ShowNodeDetails(SceneNode node)
        {
            detailsTextBox.Text = $"Node ID: {node.Id}\r\n";
            detailsTextBox.Text += $"Type: {node.Type}\r\n";

            // Display transform matrix if available
            XmlNode transformNode = node.XmlNode.SelectSingleNode("./TRANSFORM");
            if (transformNode != null && !string.IsNullOrWhiteSpace(transformNode.InnerText))
            {
                detailsTextBox.Text += "\r\nTransform Matrix:\r\n";
                string[] values = transformNode.InnerText.Trim().Split(
                    new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (values.Length >= 16)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        detailsTextBox.Text += $"[ {values[i * 4]} {values[i * 4 + 1]} {values[i * 4 + 2]} {values[i * 4 + 3]} ]\r\n";
                    }
                }
            }

            // Display bounding box if available
            XmlNode boundingBoxNode = node.XmlNode.SelectSingleNode("./BOUNDINGBOX");
            if (boundingBoxNode != null && !string.IsNullOrWhiteSpace(boundingBoxNode.InnerText))
            {
                detailsTextBox.Text += "\r\nBounding Box:\r\n";
                string[] bounds = boundingBoxNode.InnerText.Trim().Split(
                    new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (bounds.Length >= 6)
                {
                    detailsTextBox.Text += $"Min: ({bounds[0]}, {bounds[1]}, {bounds[2]})\r\n";
                    detailsTextBox.Text += $"Max: ({bounds[3]}, {bounds[4]}, {bounds[5]})\r\n";
                }
            }

            // Show LOD details if available
            if (node.Type == "LOD_LEVEL" && !string.IsNullOrEmpty(node.LodValue))
            {
                detailsTextBox.Text += $"\r\nLOD Value: {node.LodValue}\r\n";
                detailsTextBox.Text += "LOD (Level of Detail) is used to show different mesh detail\r\n";
                detailsTextBox.Text += "levels based on distance from the camera.\r\n";
                detailsTextBox.Text += "Lower values indicate models used at greater distances.\r\n";
            }
        }

        private void ShowTagDetails(string tag)
        {
            switch (tag)
            {
                case "materials":
                    detailsTextBox.Text = "Materials Section\r\n";
                    detailsTextBox.Text += "Contains a list of material shaders used by this model.";
                    break;
                case "textures":
                    detailsTextBox.Text = "Textures Section\r\n";
                    detailsTextBox.Text += "Contains a list of textures used by this material.";
                    break;
                default:
                    if (tag != null && tag.Contains(".tga", StringComparison.OrdinalIgnoreCase))
                    {
                        detailsTextBox.Text = $"Texture: {tag}\r\n";
                        detailsTextBox.Text += "Textures are used to provide surface details for 3D models.";
                    }
                    else
                    {
                        detailsTextBox.Text = "Select an item to view details";
                    }
                    break;
            }
        }

        #endregion

        #region 3D Rendering

        // Render a single material mesh
        private void RenderMaterialMesh(Material material)
        {
            try
            {
                UpdateStatus("Building material mesh...");

                // Skip if no geometry block
                if (string.IsNullOrEmpty(material.GeometryId))
                {
                    UpdateStatus("No geometry data available");
                    return;
                }

                // Get geometry block
                if (geometryBlocks.TryGetValue(material.GeometryId, out GeometryBlock block))
                {
                    // Create 3D mesh
                    Dictionary<string, XmlNode> transforms = new Dictionary<string, XmlNode>();

                    // Get transform from parent node
                    XmlNode transformNode = material.ParentNode?.SelectSingleNode("./TRANSFORM");
                    if (transformNode != null)
                    {
                        transforms[material.ParentNode.Attributes?["id"]?.Value ?? ""] = transformNode;
                    }

                    // Get bounding box for display
                    Rect3D boundingBox = GetBoundingBox(material.ParentNode);

                    // Create 3D mesh
                    Model3DGroup model = Create3DMesh(block, material.ShaderId, material.SourceId, material.Instance, transforms);
                    if (model != null)
                    {
                        modelContainer.Children.Add(new ModelVisual3D { Content = model });
                    }

                    // Add coordinate system visual
                    modelContainer.Children.Add(new CoordinateSystemVisual3D { ArrowLengths = 2 });

                    // Configure the camera
                    ConfigureCamera();

                    // Update the info panel
                    UpdateModelInfoPanel(material.ShaderId, material.GeometryId, transformNode, boundingBox,
                                      shaders.TryGetValue(material.ShaderId, out Shader shader) ? shader : null);

                    UpdateStatus("Material mesh rendered successfully");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error rendering material mesh: {ex.Message}");
            }
        }

        // Get bounding box from XML node
        private Rect3D GetBoundingBox(XmlNode node)
        {
            XmlNode boundingBoxNode = node?.SelectSingleNode("./BOUNDINGBOX");
            if (boundingBoxNode != null && !string.IsNullOrWhiteSpace(boundingBoxNode.InnerText))
            {
                string[] bounds = boundingBoxNode.InnerText.Trim().Split(
                    new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (bounds.Length >= 6)
                {
                    try
                    {
                        double minX = double.Parse(bounds[0]);
                        double minY = double.Parse(bounds[1]);
                        double minZ = double.Parse(bounds[2]);
                        double maxX = double.Parse(bounds[3]);
                        double maxY = double.Parse(bounds[4]);
                        double maxZ = double.Parse(bounds[5]);

                        return new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
                    }
                    catch { }
                }
            }
            return Rect3D.Empty;
        }

        // Update model info panel
        private void UpdateModelInfoPanel(string shaderId, string geometryId, XmlNode transformNode, Rect3D boundingBox, Shader shader = null)
        {
            modelInfoText.Text = $"Material: {shaderId}\n";
            modelInfoText.Text += $"Datablock ID: {geometryId}\n";

            // Display transform info if available
            if (transformNode != null)
            {
                Transform3D transformObj = PssgBinaryReader.ParseTransform(transformNode);
                if (transformObj is MatrixTransform3D matrixTransform)
                {
                    Matrix3D transform = matrixTransform.Matrix;
                    modelInfoText.Text += "Transform:\n";
                    modelInfoText.Text += $"  [{transform.M11:F3}, {transform.M12:F3}, {transform.M13:F3}, {transform.M14:F3}]\n";
                    modelInfoText.Text += $"  [{transform.M21:F3}, {transform.M22:F3}, {transform.M23:F3}, {transform.M24:F3}]\n";
                    modelInfoText.Text += $"  [{transform.M31:F3}, {transform.M32:F3}, {transform.M33:F3}, {transform.M34:F3}]\n";
                    modelInfoText.Text += $"  [{transform.OffsetX:F3}, {transform.OffsetY:F3}, {transform.OffsetZ:F3}, {transform.M44:F3}]\n";
                }
            }

            // Display bounding box info
            if (boundingBox != Rect3D.Empty)
            {
                modelInfoText.Text += $"Bounding Box: [{boundingBox.X:F3}, {boundingBox.Y:F3}, {boundingBox.Z:F3}] to " +
                                    $"[{boundingBox.X + boundingBox.SizeX:F3}, {boundingBox.Y + boundingBox.SizeY:F3}, {boundingBox.Z + boundingBox.SizeZ:F3}]\n";
            }

            // Add texture info if available
            if (shader != null && shader.Textures.Count > 0)
            {
                modelInfoText.Text += $"Textures: {shader.Textures.Count}\n";

                // Show textures
                for (int i = 0; i < Math.Min(3, shader.Textures.Count); i++)
                {
                    string texture = shader.Textures[i];
                    if (texture.Length > 40)
                        texture = "..." + texture.Substring(texture.Length - 37);
                    modelInfoText.Text += $"Texture {i + 1}: {texture}\n";
                }
            }
        }

        // Unified rendering approach for all node types
        private void RenderNodeModel(SceneNode node)
        {
            try
            {
                UpdateStatus($"Building node model: {node.Id}...");

                int totalVertices = 0;
                int totalTriangles = 0;

                // Dictionary to collect unique materials 
                Dictionary<string, Material> materials = new Dictionary<string, Material>();

                // Store transforms by ID to apply to model
                Dictionary<string, XmlNode> transforms = new Dictionary<string, XmlNode>();

                // Get this node's transform if available
                XmlNode transformNode = node.XmlNode.SelectSingleNode("./TRANSFORM");
                if (transformNode != null)
                {
                    transforms[node.Id] = transformNode;
                }

                // Get this node's bounding box
                Rect3D boundingBox = GetBoundingBox(node.XmlNode);

                // Fill materials dictionary based on node type
                switch (node.Type)
                {
                    case "LOD_LEVEL":
                        // For LOD_LEVEL, use the direct RenderStreamInstance children
                        FindMaterialsInNode(node.XmlNode, materials, "./RENDERSTREAMINSTANCE");
                        break;
                    case "LODVISIBLERENDERNODE":
                    case "RENDERNODE":
                        // Use direct RenderStreamInstance children
                        FindMaterialsInNode(node.XmlNode, materials, "./RENDERSTREAMINSTANCE");
                        // Check for direct child RENDERINSTANCESOURCE elements
                        ProcessDirectRenderSourcesForNode(node.XmlNode, materials);
                        break;
                }

                // Clear model info
                modelInfoText.Text = $"{node.Id} Model\n";

                // Display transform info if available
                if (transformNode != null)
                {
                    Transform3D transformObj = PssgBinaryReader.ParseTransform(transformNode);
                    if (transformObj is MatrixTransform3D matrixTransform)
                    {
                        Matrix3D transform = matrixTransform.Matrix;
                        modelInfoText.Text += "Transform:\n";
                        modelInfoText.Text += $"  [{transform.M11:F3}, {transform.M12:F3}, {transform.M13:F3}, {transform.M14:F3}]\n";
                        modelInfoText.Text += $"  [{transform.M21:F3}, {transform.M22:F3}, {transform.M23:F3}, {transform.M24:F3}]\n";
                        modelInfoText.Text += $"  [{transform.M31:F3}, {transform.M32:F3}, {transform.M33:F3}, {transform.M34:F3}]\n";
                        modelInfoText.Text += $"  [{transform.OffsetX:F3}, {transform.OffsetY:F3}, {transform.OffsetZ:F3}, {transform.M44:F3}]\n";
                    }
                }

                // Display bounding box info
                if (boundingBox != Rect3D.Empty)
                {
                    modelInfoText.Text += $"Bounding Box: [{boundingBox.X:F3}, {boundingBox.Y:F3}, {boundingBox.Z:F3}] to " +
                                        $"[{boundingBox.X + boundingBox.SizeX:F3}, {boundingBox.Y + boundingBox.SizeY:F3}, {boundingBox.Z + boundingBox.SizeZ:F3}]\n";
                }

                modelInfoText.Text += $"Materials: {materials.Count}\n";

                // Render each material
                if (materials.Count > 0)
                {
                    UpdateStatus($"Rendering {materials.Count} materials...");
                    foreach (var material in materials.Values)
                    {
                        // Set parent node if not already set
                        if (material.ParentNode == null)
                            material.ParentNode = node.XmlNode;

                        if (geometryBlocks.TryGetValue(material.GeometryId, out GeometryBlock block))
                        {
                            UpdateStatus($"Creating mesh for {material.ShaderId}...");

                            Model3DGroup model = Create3DMesh(block, material.ShaderId, material.SourceId, material.Instance, transforms);
                            if (model != null)
                            {
                                modelContainer.Children.Add(new ModelVisual3D { Content = model });

                                // Count vertices and triangles
                                if (model.Children.Count > 0 && model.Children[0] is GeometryModel3D geoModel &&
                                    geoModel.Geometry is MeshGeometry3D mesh)
                                {
                                    totalVertices += mesh.Positions.Count;
                                    totalTriangles += mesh.TriangleIndices.Count / 3;
                                }
                            }
                        }
                    }

                    // Update model info
                    modelInfoText.Text += $"Total Vertices: {totalVertices}\n";
                    modelInfoText.Text += $"Total Triangles: {totalTriangles}\n";
                }
                else
                {
                    modelInfoText.Text += "No materials found for this node.\n";

                    // Special handling for LOD parent nodes
                    if (node.Type == "LODVISIBLERENDERNODE")
                    {
                        HandleLodParentNode(node);
                    }
                }

                // Add coordinate system visual
                modelContainer.Children.Add(new CoordinateSystemVisual3D { ArrowLengths = 2 });

                // Configure the camera
                ConfigureCamera();

                UpdateStatus("Model rendered successfully");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error rendering node model: {ex.Message}");
            }
        }

        // Handle LOD parent node display
        private void HandleLodParentNode(SceneNode node)
        {
            XmlNodeList directInstances = node.XmlNode.SelectNodes("./RENDERSTREAMINSTANCE");
            if (directInstances != null && directInstances.Count > 0)
            {
                modelInfoText.Text += $"Direct RENDERSTREAMINSTANCE nodes: {directInstances.Count}\n";

                // Debug output for instance nodes
                foreach (XmlNode instance in directInstances)
                {
                    string shaderId = instance.Attributes?["shader"]?.Value;
                    if (string.IsNullOrEmpty(shaderId)) continue;

                    shaderId = shaderId.TrimStart('#');
                    modelInfoText.Text += $"  Instance shader: {shaderId}\n";

                    // Log source refs
                    XmlNodeList sourceRefs = instance.SelectNodes(".//RENDERINSTANCESOURCE");
                    if (sourceRefs != null && sourceRefs.Count > 0)
                    {
                        foreach (XmlNode sourceRef in sourceRefs)
                        {
                            string sourceId = sourceRef.Attributes?["source"]?.Value;
                            if (string.IsNullOrEmpty(sourceId)) continue;

                            sourceId = sourceId.TrimStart('#');
                            modelInfoText.Text += $"    Source ref: {sourceId}\n";

                            // Try to find geometry block
                            string geometryId = FindGeometryForSource(sourceId);
                            if (!string.IsNullOrEmpty(geometryId))
                                modelInfoText.Text += $"      Found geometry: {geometryId}\n";
                            else
                                modelInfoText.Text += $"      No geometry found\n";
                        }
                    }
                    else
                    {
                        modelInfoText.Text += $"    No source refs found\n";
                    }
                }
            }

            // Check for LOD instances
            XmlNode lodInstances = node.XmlNode.SelectSingleNode("./LODRENDERINSTANCES");
            if (lodInstances != null)
            {
                XmlNodeList lodLevels = lodInstances.SelectNodes("./LODRENDERINSTANCELIST");
                if (lodLevels != null && lodLevels.Count > 0)
                {
                    modelInfoText.Text += $"LOD levels found: {lodLevels.Count}\n";
                    modelInfoText.Text += "Please select a specific LOD level to view its 3D content.\n";
                }
            }
        }

        // Process direct render sources for a node
        private void ProcessDirectRenderSourcesForNode(XmlNode node, Dictionary<string, Material> materials)
        {
            try
            {
                // Look for direct RENDERSTREAMINSTANCE elements
                XmlNodeList instances = node.SelectNodes("./RENDERSTREAMINSTANCE");
                if (instances == null || instances.Count == 0) return;

                foreach (XmlNode instance in instances)
                {
                    string shaderId = instance.Attributes?["shader"]?.Value;
                    if (string.IsNullOrEmpty(shaderId)) continue;

                    shaderId = shaderId.TrimStart('#');

                    // Find source reference
                    XmlNodeList sourceRefs = instance.SelectNodes(".//RENDERINSTANCESOURCE");
                    if (sourceRefs == null || sourceRefs.Count == 0) continue;

                    foreach (XmlNode sourceRef in sourceRefs)
                    {
                        string sourceId = sourceRef.Attributes?["source"]?.Value;
                        if (string.IsNullOrEmpty(sourceId)) continue;

                        sourceId = sourceId.TrimStart('#');

                        // Find geometry block for this source
                        string geometryId = FindGeometryForSource(sourceId);
                        if (string.IsNullOrEmpty(geometryId)) continue;

                        // Create a unique key combining shader and source
                        string key = $"{shaderId}_{sourceId}";

                        // Only add if not already added
                        if (!materials.ContainsKey(key))
                        {
                            materials[key] = new Material
                            {
                                ShaderId = shaderId,
                                GeometryId = geometryId,
                                SourceId = sourceId,
                                Instance = instance,
                                ParentNode = node
                            };
                        }
                    }
                }
            }
            catch { /* Ignore errors in this helper */ }
        }

        // Helper method to find materials in a node
        private void FindMaterialsInNode(XmlNode node, Dictionary<string, Material> materials, string xpath)
        {
            XmlNodeList renderInstances = node.SelectNodes(xpath);
            if (renderInstances == null || renderInstances.Count == 0) return;

            foreach (XmlNode instance in renderInstances)
            {
                string shaderId = instance.Attributes?["shader"]?.Value;
                if (string.IsNullOrEmpty(shaderId)) continue;

                shaderId = shaderId.TrimStart('#');

                // Find source reference
                XmlNode sourceRef = instance.SelectSingleNode("./RENDERINSTANCESOURCE");
                if (sourceRef == null) continue;

                string sourceId = sourceRef.Attributes?["source"]?.Value;
                if (string.IsNullOrEmpty(sourceId)) continue;

                sourceId = sourceId.TrimStart('#');

                // Find geometry block for this source
                string geometryId = FindGeometryForSource(sourceId);
                if (string.IsNullOrEmpty(geometryId)) continue;

                Material material = new Material
                {
                    ShaderId = shaderId,
                    GeometryId = geometryId,
                    SourceId = sourceId,
                    Instance = instance,
                    ParentNode = node
                };

                // Use a unique key combining shader and source
                string key = $"{shaderId}_{sourceId}";
                if (!materials.ContainsKey(key))
                {
                    materials[key] = material;
                }
            }
        }

        // Configure camera settings
        private void ConfigureCamera()
        {
            // Setup camera mode
            modelView.CameraMode = CameraMode.Inspect;
            modelView.Camera.UpDirection = cameraUpDirection; // Lock roll

            // Set camera controls
            modelView.RotationSensitivity = 1.0;
            modelView.ZoomSensitivity = 1.0;

            // Custom mouse handling
            modelView.MouseDown -= ModelView_MouseDown;
            modelView.MouseMove -= ModelView_MouseMove;
            modelView.MouseUp -= ModelView_MouseUp;
            modelView.MouseWheel -= ModelView_MouseWheel;

            modelView.MouseDown += ModelView_MouseDown;
            modelView.MouseMove += ModelView_MouseMove;
            modelView.MouseUp += ModelView_MouseUp;
            modelView.MouseWheel += ModelView_MouseWheel;

            // Reset camera to a good viewing position
            modelView.ResetCamera();
            modelView.ZoomExtents();
        }

        private void ModelView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                isRotating = true;
                lastMousePosition = e.GetPosition(modelView);
                modelView.CaptureMouse();
                e.Handled = true;
            }
        }

        private void ModelView_MouseMove(object sender, MouseEventArgs e)
        {
            if (isRotating)
            {
                Point currentPosition = e.GetPosition(modelView);
                Vector delta = currentPosition - lastMousePosition;

                // Calculate rotation angles based on mouse movement
                double yaw = delta.X * 0.5;
                double pitch = delta.Y * 0.5;

                // Apply rotations while maintaining up direction
                RotateCamera(yaw, pitch);

                lastMousePosition = currentPosition;
                e.Handled = true;
            }
        }

        private void ModelView_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Released && isRotating)
            {
                isRotating = false;
                modelView.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void ModelView_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Handle zoom with mouse wheel
            double zoomFactor = e.Delta > 0 ? 0.9 : 1.1;

            if (modelView.Camera is PerspectiveCamera perspectiveCamera)
            {
                // Calculate zoom based on field of view
                perspectiveCamera.FieldOfView *= zoomFactor;

                // Clamp field of view to reasonable values
                perspectiveCamera.FieldOfView = Math.Max(10, Math.Min(90, perspectiveCamera.FieldOfView));
            }

            e.Handled = true;
        }

        // Rotate camera with roll locked
        private void RotateCamera(double yaw, double pitch)
        {
            // Get current camera look direction and position
            WinVector lookDirection = modelView.Camera.LookDirection;

            // Create rotation quaternions for yaw and pitch
            WinQuaternion yawRotation = new WinQuaternion(cameraUpDirection, yaw);

            // Compute the right vector (perpendicular to look and up)
            WinVector right = Vector3D.CrossProduct(lookDirection, cameraUpDirection);
            right.Normalize();

            // Create pitch rotation around the right vector
            WinQuaternion pitchRotation = new WinQuaternion(right, pitch);

            // Combine rotations and apply
            WinQuaternion totalRotation = WinQuaternion.Multiply(yawRotation, pitchRotation);

            // Rotate the look direction
            Matrix3D rotationMatrix = Matrix3D.Identity;
            rotationMatrix.Rotate(totalRotation);

            WinVector newLookDirection = rotationMatrix.Transform(lookDirection);

            // Ensure we're not looking exactly up or down to prevent gimbal lock
            WinVector upDirection = cameraUpDirection;
            double upDot = Vector3D.DotProduct(newLookDirection, upDirection);

            if (Math.Abs(upDot) > 0.999)
                return; // Close to parallel with up vector, limit rotation

            // Apply the new look direction while keeping up direction fixed
            modelView.Camera.LookDirection = newLookDirection;
            modelView.Camera.UpDirection = cameraUpDirection; // Keep up direction locked
        }

        private Model3DGroup Create3DMesh(GeometryBlock block, string shaderId, string sourceId, XmlNode instance, Dictionary<string, XmlNode> transforms = null)
        {
            try
            {
                // Create material with strong color for visibility
                Color color = GetMaterialColor(shaderId);
                DiffuseMaterial material = new DiffuseMaterial(new SolidColorBrush(color));

                // Extract geometry data using PssgBinaryReader
                Point3DCollection rawPositions = new Point3DCollection();
                Vector3DCollection rawNormals = new Vector3DCollection();
                PointCollection texCoords = new PointCollection();
                Int32Collection indices = new Int32Collection();

                // Parse vertex data
                PssgBinaryReader.ParseGeometryData(block.XmlNode, block, out rawPositions, out rawNormals, out texCoords);

                // Convert coordinates by swapping Y and Z
                Point3DCollection positions = new Point3DCollection();
                Vector3DCollection normals = new Vector3DCollection();

                foreach (Point3D pos in rawPositions)
                    positions.Add(new Point3D(pos.X, pos.Z, pos.Y)); // Swap Y and Z

                foreach (Vector3D norm in rawNormals)
                    normals.Add(new Vector3D(norm.X, norm.Z, norm.Y)); // Swap Y and Z

                // Skip if no vertices
                if (positions.Count == 0)
                {
                    UpdateStatus("No vertex data found or parsing failed");
                    return null;
                }

                // Get index data if source provided
                if (!string.IsNullOrEmpty(sourceId))
                {
                    XmlDocument document = new XmlDocument();
                    document.Load(txtFilePath.Text);

                    // Find the render data source
                    XmlNode sourceNode = document.SelectSingleNode($"//RENDERDATASOURCE[@id='{sourceId}']");

                    // If not found directly, try with relaxed matching
                    if (sourceNode == null)
                    {
                        foreach (XmlNode node in document.SelectNodes("//RENDERDATASOURCE"))
                        {
                            string id = node.Attributes?["id"]?.Value;
                            if (!string.IsNullOrEmpty(id) && (id == sourceId || id.StartsWith(sourceId + "_")))
                            {
                                sourceNode = node;
                                break;
                            }
                        }
                    }

                    if (sourceNode != null)
                    {
                        XmlNode indexNode = sourceNode.SelectSingleNode(".//RENDERINDEXSOURCE");
                        if (indexNode != null)
                        {
                            XmlNode indexData = indexNode.SelectSingleNode("./INDEXSOURCEDATA");
                            if (indexData != null)
                            {
                                indices = PssgBinaryReader.ParseIndices(indexData, indexNode);
                            }
                        }
                    }
                }

                // Generate default indices if none found
                if (indices.Count == 0 && positions.Count >= 3)
                {
                    // For triangle primitives
                    for (int i = 0; i < positions.Count; i += 3)
                    {
                        if (i + 2 < positions.Count)
                        {
                            indices.Add(i);
                            indices.Add(i + 1);
                            indices.Add(i + 2);
                        }
                    }

                    // If we still have no indices, just connect sequential vertices
                    if (indices.Count == 0)
                    {
                        for (int i = 0; i < positions.Count; i++)
                            indices.Add(i);
                    }
                }

                // Create geometry
                MeshGeometry3D geometry = new MeshGeometry3D
                {
                    Positions = positions,
                    TriangleIndices = indices
                };

                // Add normals if available
                if (normals.Count == positions.Count)
                    geometry.Normals = normals;
                else if (positions.Count > 0)
                {
                    // Generate simple normals if none available
                    Vector3DCollection generatedNormals = new Vector3DCollection();
                    for (int i = 0; i < positions.Count; i++)
                        generatedNormals.Add(new Vector3D(0, 0, 1));
                    geometry.Normals = generatedNormals;
                }

                // Add texture coordinates if available
                if (texCoords.Count == positions.Count)
                    geometry.TextureCoordinates = texCoords;

                // Create model
                Model3DGroup group = new Model3DGroup();

                // Add the main model
                GeometryModel3D model = new GeometryModel3D
                {
                    Geometry = geometry,
                    Material = material,
                    BackMaterial = material
                };
                group.Children.Add(model);

                // Add a wireframe version
                if (positions.Count > 0)
                    AddWireframeToModel(group, indices, positions);

                // Apply transform from instance or parent node
                Transform3D transform = null;

                // First check if we have a transform on this instance
                if (instance != null)
                {
                    XmlNode parentNode = instance.ParentNode;
                    if (parentNode != null)
                    {
                        XmlNode transformNode = parentNode.SelectSingleNode("./TRANSFORM");
                        if (transformNode != null)
                        {
                            transform = PssgBinaryReader.ParseTransform(transformNode);
                        }
                    }
                }

                // If no transform from instance, try transforms dictionary
                if (transform == null && transforms != null && transforms.Count > 0)
                {
                    // Try to apply transforms from parent nodes
                    foreach (var t in transforms)
                    {
                        transform = PssgBinaryReader.ParseTransform(t.Value);
                        if (transform != null)
                            break;
                    }
                }

                // Apply transform to model
                if (transform != null)
                    group.Transform = transform;

                return group;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error creating mesh: {ex.Message}");
                return null;
            }
        }

        // Add wireframe to a model
        private void AddWireframeToModel(Model3DGroup group, Int32Collection indices, Point3DCollection positions)
        {
            MeshGeometry3D wireframeGeometry = new MeshGeometry3D();

            // Create lines for all edges
            for (int i = 0; i < indices.Count; i += 3)
            {
                if (i + 2 < indices.Count)
                {
                    int a = indices[i];
                    int b = indices[i + 1];
                    int c = indices[i + 2];

                    // Only add lines if indices are valid
                    if (a < positions.Count && b < positions.Count && c < positions.Count)
                    {
                        // Add edges as lines
                        AddLineToGeometry(wireframeGeometry, positions[a], positions[b]);
                        AddLineToGeometry(wireframeGeometry, positions[b], positions[c]);
                        AddLineToGeometry(wireframeGeometry, positions[c], positions[a]);
                    }
                }
            }

            if (wireframeGeometry.Positions.Count > 0)
            {
                GeometryModel3D wireframe = new GeometryModel3D
                {
                    Geometry = wireframeGeometry,
                    Material = new DiffuseMaterial(Brushes.Black)
                };
                group.Children.Add(wireframe);
            }
        }

        // Helper method to add a line to a mesh geometry
        private void AddLineToGeometry(MeshGeometry3D geometry, Point3D p1, Point3D p2)
        {
            // Add a thin rectangular prism between the points to represent a line
            double thickness = 0.01;

            // Create a direction vector
            WinVector dir = p2 - p1;

            // Create perpendicular vectors
            WinVector up = new WinVector(0, 0, 1);
            if (Math.Abs(Vector3D.DotProduct(dir, up)) > 0.9)
                up = new WinVector(1, 0, 0);

            WinVector right = Vector3D.CrossProduct(dir, up);
            right.Normalize();
            right *= thickness / 2;

            up = Vector3D.CrossProduct(right, dir);
            up.Normalize();
            up *= thickness / 2;

            // Calculate the 8 corners of the rectangular prism
            int baseIndex = geometry.Positions.Count;

            // Add the positions
            Point3D[] corners = new Point3D[]
            {
                p1 + right + up, p1 + right - up, p1 - right - up, p1 - right + up,
                p2 + right + up, p2 + right - up, p2 - right - up, p2 - right + up
            };

            foreach (var corner in corners)
                geometry.Positions.Add(corner);

            // Add the triangles (12 triangles for a cube)
            int[][] faces = new int[][]
            {
                new int[] {0, 1, 2, 2, 3, 0},   // Front
                new int[] {4, 7, 6, 6, 5, 4},   // Back
                new int[] {0, 3, 7, 7, 4, 0},   // Side 1
                new int[] {1, 5, 6, 6, 2, 1},   // Side 2
                new int[] {0, 4, 5, 5, 1, 0},   // Side 3
                new int[] {2, 6, 7, 7, 3, 2}    // Side 4
            };

            foreach (var face in faces)
                foreach (var idx in face)
                    geometry.TriangleIndices.Add(baseIndex + idx);
        }

        private Color GetMaterialColor(string shaderId)
        {
            // Generate color from shader ID
            if (string.IsNullOrEmpty(shaderId))
                return Colors.LightGray;

            // Use hash code for deterministic color
            int hash = shaderId.GetHashCode();
            byte r = (byte)((hash & 0xFF0000) >> 16);
            byte g = (byte)((hash & 0x00FF00) >> 8);
            byte b = (byte)(hash & 0x0000FF);

            // Ensure not too dark
            r = (byte)Math.Max((int)r, 64);
            g = (byte)Math.Max((int)g, 64);
            b = (byte)Math.Max((int)b, 64);

            return Color.FromRgb(r, g, b);
        }

        #endregion

        private void UpdateStatus(string message)
        {
            statusText.Text = message;
        }
    }

    #region Data Classes

    public class SceneNode
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public XmlNode XmlNode { get; set; }
        public string LodValue { get; set; }
        public bool IsModelNode { get; set; } = false;
    }

    public class Material
    {
        public string ShaderId { get; set; }
        public string GeometryId { get; set; }
        public string SourceId { get; set; }
        public XmlNode Instance { get; set; }
        public XmlNode ParentNode { get; set; }
    }

    public class Shader
    {
        public string Id { get; set; }
        public string ShaderGroup { get; set; }
        public List<string> Textures { get; set; } = new List<string>();
    }

    public class GeometryBlock
    {
        public string Id { get; set; }
        public int ElementCount { get; set; }
        public int StreamCount { get; set; }
        public List<DataStream> Streams { get; set; } = new List<DataStream>();
        public XmlNode XmlNode { get; set; }
    }

    public class DataStream
    {
        public string RenderType { get; set; }
        public string DataType { get; set; }
        public int Offset { get; set; }
        public int Stride { get; set; }
    }

    #endregion
}