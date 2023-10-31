﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BlueprintEditor.Models.Connections;
using BlueprintEditor.Models.Types;
using BlueprintEditor.Models.Types.EbxEditorTypes;
using BlueprintEditor.Models.Types.NodeTypes;
using BlueprintEditor.Models.Types.NodeTypes.Shared;
using BlueprintEditor.Utils;
using Frosty.Core;
using Frosty.Core.Controls;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using Nodify;
using Prism.Commands;

namespace BlueprintEditor.Models.Editor
{

    #region Editor

    /// <summary>
    /// This is the editor itself. This collects all of the Nodes and Connections needed to be made
    /// </summary>
    public class EditorViewModel
    {
        public ObservableCollection<NodeBaseModel> Nodes { get; } = new ObservableCollection<NodeBaseModel>();
        
        public ObservableCollection<NodeBaseModel> SelectedNodes { get; } = new ObservableCollection<NodeBaseModel>();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new ObservableCollection<ConnectionViewModel>();
        public PendingConnectionViewModel PendingConnection { get; }
        public SolidColorBrush PendingConnectionColor { get; } = new SolidColorBrush(Colors.White);
        public ICommand DisconnectConnectorCommand { get; }

        public string EditorName { get; }

        private readonly EbxBaseEditor _ebxEditor;
        public BlueprintPropertyGrid NodePropertyGrid { get; set; }
        public BlueprintPropertyGrid InterfacePropertyGrid { get; set; }
        public EbxAsset EditedEbxAsset { get; set; }
        public dynamic EditedProperties => EditedEbxAsset.RootObject;
        public AssetClassGuid InterfaceGuid { get; private set; }

        public EditorViewModel()
        {
            EditedEbxAsset = App.AssetManager.GetEbx((EbxAssetEntry)App.EditorWindow.GetOpenedAssetEntry());
            EditorName = App.EditorWindow.GetOpenedAssetEntry().Filename;
            
            EbxBaseEditor ebxEditor = new EbxBaseEditor();
            foreach (var type in Assembly.GetCallingAssembly().GetTypes())
            {
                if (type.IsSubclassOf(typeof(EbxBaseEditor)))
                {
                    EbxBaseEditor extension = (EbxBaseEditor)Activator.CreateInstance(type);
                    if (extension.AssetType != App.EditorWindow.GetOpenedAssetEntry().Type) continue;
                    ebxEditor = extension;
                    break;
                }
            }

            ebxEditor.NodeEditor = this;
            _ebxEditor = ebxEditor;

            PendingConnection = new PendingConnectionViewModel(this, _ebxEditor);
            
            DisconnectConnectorCommand = new DelegateCommand<Object>(connector =>
            {
                //ConnectionViewModel connection = connector.GetType().Name == "InputViewModel" ? Connections.First(x => x.Target == connector) : Connections.First(x => x.Source == connector);
                Disconnect(connector.GetType().Name == "InputViewModel"
                    ? Connections.First(x => x.Target == connector)
                    : Connections.First(x => x.Source == connector));
            });
            
            EditorUtils.Editors.Add(EditorName, this);
        }

        public event EventHandler<EditorStatusArgs> EditorStatusChanged;
        
        /// <summary>
        /// Sets the Editor's problem status.
        /// </summary>
        /// <param name="status"></param>
        /// <param name="id"></param>
        /// <param name="toolTip"></param>
        public void SetEditorStatus(EditorStatus status, int id, string toolTip = null)
        {
            EditorStatusChanged?.Invoke(this, new EditorStatusArgs(status, id, toolTip));
        }
        
        public event EventHandler<EditorStatusArgs> RemoveEditorStatus;
        /// <summary>
        /// Removes a problem ID of a certain status from the list of problems.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="status"></param>
        public void ResetEditorStatus(int id, EditorStatus status)
        {
            RemoveEditorStatus?.Invoke(this, new EditorStatusArgs(status, id));
        }

        #region Node Editing

        /// <summary>
        /// This will create a new node from an object
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public NodeBaseModel CreateNodeFromObject(object obj)
        {
            string key = obj.GetType().Name;
            NodeBaseModel newNode;
            
            if (NodeUtils.NodeExtensions.ContainsKey(key))
            {
                newNode = (NodeBaseModel)Activator.CreateInstance(NodeUtils.NodeExtensions[key].GetType());
            }
            else if (NodeUtils.NmcExtensions.ContainsKey(key))
            {
                newNode = new NodeBaseModel();
                if (NodeUtils.NmcExtensions[key].All(arg => arg.Split('=')[0] != "ValidGameExecutableName")
                    || NodeUtils.NmcExtensions[key].Any(arg => arg == $"ValidGameExecutableName={ProfilesLibrary.ProfileName}"))
                {
                    foreach (string arg in NodeUtils.NmcExtensions[key])
                    {
                        switch (arg.Split('=')[0])
                        {
                            case "DisplayName":
                            {
                                newNode.Name = arg.Split('=')[1];
                            } break;
                            case "InputEvent":
                            {
                                newNode.Inputs.Add(new InputViewModel() { Title = arg.Split('=')[1], Type = ConnectionType.Event });
                            } break;
                            case "InputProperty":
                            {
                                newNode.Inputs.Add(new InputViewModel() { Title = arg.Split('=')[1], Type = ConnectionType.Property });
                            } break;
                            case "InputLink":
                            {
                                newNode.Inputs.Add(new InputViewModel() { Title = arg.Split('=')[1], Type = ConnectionType.Link });
                            } break;
                            case "OutputEvent":
                            {
                                newNode.Outputs.Add(new OutputViewModel() { Title = arg.Split('=')[1], Type = ConnectionType.Event });
                            } break;
                            case "OutputProperty":
                            {
                                newNode.Outputs.Add(new OutputViewModel() { Title = arg.Split('=')[1], Type = ConnectionType.Property });
                            } break;
                            case "OutputLink":
                            {
                                newNode.Outputs.Add(new OutputViewModel() { Title = arg.Split('=')[1], Type = ConnectionType.Link });
                            } break;
                        }
                    }
                }
            }
            else //We could not find an extension
            {
                newNode = new NodeBaseModel();
                newNode.Inputs = NodeUtils.GenerateNodeInputs(obj.GetType(), newNode);
                newNode.Name = obj.GetType().Name;
            }
            
            newNode.Object = obj;
            newNode.Guid = ((dynamic)obj).GetInstanceGuid();
            newNode.OnCreation();

            Nodes.Add(newNode);
            return newNode;
        }

        public object CreateNodeObject(Type type) => _ebxEditor.AddNodeObject(type);
        public object CreateNodeObject(object obj) => _ebxEditor.AddNodeObject(obj);

        /// <summary>
        /// Deletes a node(and by extension all of its connections).
        /// </summary>
        /// <param name="node">Node to delete</param>
        public void DeleteNode(NodeBaseModel node)
        {
            #region Interface removal

            if (node.ObjectType == "EditorInterfaceNode")
            {
                //Is this in or out?
                if (node.Inputs.Count != 0)
                {
                    var input = node.Inputs[0];
                    
                    foreach (ConnectionViewModel connection in GetConnections(input))
                    {
                        Disconnect(connection);
                    }
                    
                    switch (input.Type)
                    {
                        case ConnectionType.Property:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditedProperties.Interface.Internal.Fields)
                            {
                                if (field.Name.ToString() == input.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditedProperties.Interface.Internal.Fields.Remove(objToRemove);
                            }
                            break;
                        }
                        case ConnectionType.Event:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditedProperties.Interface.Internal.OutputEvents)
                            {
                                if (field.Name.ToString() == input.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditedProperties.Interface.Internal.OutputEvents.Remove(objToRemove);
                            }
                            break;
                        }
                        case ConnectionType.Link:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditedProperties.Interface.Internal.OutputLinks)
                            {
                                if (field.Name.ToString() == input.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditedProperties.Interface.Internal.OutputLinks.Remove(objToRemove);
                            }
                            break;
                        }
                    }
                }
                
                else
                {
                    var output = node.Outputs[0];
                    
                    foreach (ConnectionViewModel connection in GetConnections(output))
                    {
                        Disconnect(connection);
                    }
                    
                    switch (output.Type)
                    {
                        case ConnectionType.Property:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditedProperties.Interface.Internal.Fields)
                            {
                                if (field.Name.ToString() == output.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditedProperties.Interface.Internal.Fields.Remove(objToRemove);
                            }
                            break;
                        }
                        case ConnectionType.Event:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditedProperties.Interface.Internal.InputEvents)
                            {
                                if (field.Name.ToString() == output.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditedProperties.Interface.Internal.InputEvents.Remove(objToRemove);
                            }
                            break;
                        }
                        case ConnectionType.Link:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditedProperties.Interface.Internal.InputLinks)
                            {
                                if (field.Name.ToString() == output.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditedProperties.Interface.Internal.InputLinks.Remove(objToRemove);
                            }
                            break;
                        }
                    }
                }

                Nodes.Remove(node);
                
                App.AssetManager.ModifyEbx(App.AssetManager.GetEbxEntry(EditedEbxAsset.FileGuid).Name, EditedEbxAsset);
                App.EditorWindow.DataExplorer.RefreshItems();
                
                InterfacePropertyGrid.Object = new object();
                InterfacePropertyGrid.Object = EditedProperties.Interface.Internal;
                return;
            }

            #endregion

            #region Object Removal
            
            foreach (ConnectionViewModel connection in GetConnections(node))
            {
                Disconnect(connection);
            }

            _ebxEditor.RemoveNodeObject(node);
            
            Nodes.Remove(node);
            
            App.AssetManager.ModifyEbx(App.AssetManager.GetEbxEntry(EditedEbxAsset.FileGuid).Name, EditedEbxAsset);
            App.EditorWindow.DataExplorer.RefreshItems();

            #endregion
        }

        public bool EditNodeProperties(object nodeObj, ItemModifiedEventArgs args)
        {
            bool worked = _ebxEditor.EditEbx(nodeObj, args);
            App.AssetManager.ModifyEbx(App.AssetManager.GetEbxEntry(EditedEbxAsset.FileGuid).Name, EditedEbxAsset);
            App.EditorWindow.DataExplorer.RefreshItems();
            return worked;
        }

        #endregion

        #region Interfaces

        /// <summary>
        /// Creates an interface node from a InterfaceDescriptorData
        /// </summary>
        /// <param name="obj">InterfaceDescriptorData</param>
        /// <returns></returns>
        public void CreateInterfaceNodes(object obj)
        {
            InterfaceGuid = ((dynamic)obj).GetInstanceGuid();
            
            foreach (dynamic field in ((dynamic)obj).Fields)
            {
                if (field.AccessType.ToString() == "FieldAccessType_Source") //Source
                {
                    InterfaceDataNode node = InterfaceDataNode.CreateInterfaceDataNode(field, false, InterfaceGuid);
                    node.Object = obj;
                    if (!InterfaceInputDataNodes.ContainsKey(node.Inputs[0].Title))
                    {
                        Nodes.Add(node);
                        InterfaceInputDataNodes.Add(node.Inputs[0].Title, node);
                    }
                }
                else if (field.AccessType.ToString() == "FieldAccessType_Target") //Target
                {
                    InterfaceDataNode node = InterfaceDataNode.CreateInterfaceDataNode(field, true, InterfaceGuid);
                    node.Object = obj;
                    if (!InterfaceOutputDataNodes.ContainsKey(node.Outputs[0].Title))
                    {
                        Nodes.Add(node);
                        InterfaceOutputDataNodes.Add(node.Outputs[0].Title, node);
                    }
                }
                else //Source and Target
                {
                    InterfaceDataNode node = InterfaceDataNode.CreateInterfaceDataNode(field, true, InterfaceGuid);
                    node.Object = obj;
                    if (!InterfaceOutputDataNodes.ContainsKey(node.Outputs[0].Title))
                    {
                        Nodes.Add(node);
                        InterfaceOutputDataNodes.Add(node.Outputs[0].Title, node);
                    }
                    
                    node = InterfaceDataNode.CreateInterfaceDataNode(field, false, InterfaceGuid);
                    node.Object = obj;
                    if (!InterfaceInputDataNodes.ContainsKey(node.Inputs[0].Title))
                    {
                        Nodes.Add(node);
                        InterfaceInputDataNodes.Add(node.Inputs[0].Title, node);
                    }
                }
            }

            foreach (dynamic inputEvent in ((dynamic)obj).InputEvents)
            {
                InterfaceDataNode node = InterfaceDataNode.CreateInterfaceDataNode(inputEvent, true, InterfaceGuid);
                node.Object = obj;
                if (!InterfaceOutputDataNodes.ContainsKey(node.Outputs[0].Title))
                {
                    Nodes.Add(node);
                    InterfaceOutputDataNodes.Add(node.Outputs[0].Title, node);
                }
            }
                
            foreach (dynamic outputEvent in ((dynamic)obj).OutputEvents)
            {
                InterfaceDataNode node = InterfaceDataNode.CreateInterfaceDataNode(outputEvent, false, InterfaceGuid);
                node.Object = obj;
                if (!InterfaceInputDataNodes.ContainsKey(node.Inputs[0].Title))
                {
                    Nodes.Add(node);
                    InterfaceInputDataNodes.Add(node.Inputs[0].Title, node);
                }
            }
                
            foreach (dynamic inputLink in ((dynamic)obj).InputLinks)
            {
                InterfaceDataNode node = InterfaceDataNode.CreateInterfaceDataNode(inputLink, true, InterfaceGuid);
                node.Object = obj;
                if (!InterfaceOutputDataNodes.ContainsKey(node.Outputs[0].Title))
                {
                    Nodes.Add(node);
                    InterfaceOutputDataNodes.Add(node.Outputs[0].Title, node);
                }
            }
                
            foreach (dynamic outputLink in ((dynamic)obj).OutputLinks)
            {
                InterfaceDataNode node = InterfaceDataNode.CreateInterfaceDataNode(outputLink, false, InterfaceGuid);
                node.Object = obj;
                if (!InterfaceInputDataNodes.ContainsKey(node.Inputs[0].Title))
                {
                    Nodes.Add(node);
                    InterfaceInputDataNodes.Add(node.Inputs[0].Title, node);
                }
            }
        }
        
        /// <summary>
        /// Recreates the list of interface nodes based off of the old and new interface
        /// </summary>
        /// <param name="newObj">InterfaceDescriptorData</param>
        /// <returns></returns>
        public void RefreshInterfaceNodes(object newObj)
        {
            CreateInterfaceNodes(newObj);

            List<string> interfaceNodesToRemove = new List<string>();
            foreach (InterfaceDataNode interfaceDataNode in InterfaceInputDataNodes.Values.Where(interfaceDataNode =>
                         (interfaceDataNode.InterfaceItem.GetType().Name == "DataField" && !((dynamic)newObj).Fields.Contains(interfaceDataNode.InterfaceItem))
                         || (interfaceDataNode.InterfaceItem.GetType().Name == "DynamicEvent" && !((dynamic)newObj).OutputEvents.Contains(interfaceDataNode.InterfaceItem))
                         || (interfaceDataNode.InterfaceItem.GetType().Name == "DynamicLink" && !((dynamic)newObj).OutputLinks.Contains(interfaceDataNode.InterfaceItem))))
            {
                DeleteNode(interfaceDataNode);
                interfaceNodesToRemove.Add(interfaceDataNode.Inputs[0].Title);
            }
            foreach (InterfaceDataNode interfaceDataNode in InterfaceOutputDataNodes.Values.Where(interfaceDataNode =>
                         (interfaceDataNode.InterfaceItem.GetType().Name == "DataField" && !((dynamic)newObj).Fields.Contains(interfaceDataNode.InterfaceItem))
                         || (interfaceDataNode.InterfaceItem.GetType().Name == "DynamicEvent" && !((dynamic)newObj).InputEvents.Contains(interfaceDataNode.InterfaceItem))
                         || (interfaceDataNode.InterfaceItem.GetType().Name == "DynamicLink" && !((dynamic)newObj).InputLinks.Contains(interfaceDataNode.InterfaceItem))))
            {
                DeleteNode(interfaceDataNode);
                interfaceNodesToRemove.Add(interfaceDataNode.Outputs[0].Title);
            }

            foreach (var remove in interfaceNodesToRemove)
            {
                InterfaceInputDataNodes.Remove(remove);
                InterfaceOutputDataNodes.Remove(remove);
            }
        }
        
        public Dictionary<string, InterfaceDataNode> InterfaceInputDataNodes = new Dictionary<string, InterfaceDataNode>();
        public Dictionary<string, InterfaceDataNode> InterfaceOutputDataNodes = new Dictionary<string, InterfaceDataNode>();

        #endregion

        #region Connecting and Disconnecting nodes

        /// <summary>
        /// Connect 2 nodes together.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="target"></param>
        public ConnectionViewModel Connect(OutputViewModel source, InputViewModel target)
        {
            var connection = new ConnectionViewModel(source, target, source.Type);
            if (Connections.All(x => x.Source != connection.Source || x.Target != connection.Target))
            {
                Connections.Add(connection);
                connection.TargetNode.OnInputUpdated(target);
                connection.SourceNode.OnOutputUpdated(source);
            }

            return connection;
        }

        public void CreateConnectionObject(ConnectionViewModel connection) => _ebxEditor.CreateConnectionObject(connection);

        /// <summary>
        /// Removes a connection
        /// </summary>
        /// <param name="connection"></param>
        public void Disconnect(ConnectionViewModel connection)
        {
            App.EditorWindow.OpenAsset(App.AssetManager.GetEbxEntry(EditedEbxAsset.FileGuid));
            
            bool sourceConnected = false;
            bool targetConnected = false;
            foreach (ConnectionViewModel connectionViewModel in Connections)
            {
                if (connectionViewModel == connection) continue;
                
                if (connection.Source == connectionViewModel.Source)
                {
                    sourceConnected = true;
                }

                if (connection.Target == connectionViewModel.Target)
                {
                    targetConnected = true;
                }
            }
            connection.Source.IsConnected = sourceConnected;
            connection.Target.IsConnected = targetConnected;
            
            _ebxEditor.RemoveConnectionObject(connection);

            App.AssetManager.ModifyEbx(App.AssetManager.GetEbxEntry(EditedEbxAsset.FileGuid).Name, EditedEbxAsset);
            App.EditorWindow.DataExplorer.RefreshItems();
            Connections.Remove(connection);
        }

        #endregion

        #region Getting Connections

        /// <summary>
        /// Gets a list of connections with this <see cref="InputViewModel"/>
        /// </summary>
        /// <param name="inputViewModel">The input view model to find</param>
        /// <returns>A list of all connections found</returns>
        public List<ConnectionViewModel> GetConnections(InputViewModel inputViewModel)
        {
            List<ConnectionViewModel> connections = new List<ConnectionViewModel>();
            Parallel.ForEach(Connections, connection =>
            {
                if (connection.Target == inputViewModel && !connections.Contains(connection))
                {
                    connections.Add(connection);
                }
            });
            return connections;
        }

        /// <summary>
        /// Gets a list of connections with this <see cref="OutputViewModel"/>
        /// </summary>
        /// <param name="output">The output view model to find</param>
        /// <returns>A list of all connections found</returns>
        public List<ConnectionViewModel> GetConnections(OutputViewModel output)
        {
            List<ConnectionViewModel> connections = new List<ConnectionViewModel>();
            Parallel.ForEach(Connections, connection =>
            {
                if (connection.Source == output && !connections.Contains(connection))
                {
                    connections.Add(connection);
                }
            });
            return connections;
        }

        /// <summary>
        /// Gets a list of connections with this <see cref="OutputViewModel"/>
        /// </summary>
        /// <param name="node"></param>
        /// <returns>A list of all connections found</returns>
        public List<ConnectionViewModel> GetConnections(NodeBaseModel node)
        {
            List<ConnectionViewModel> connections = new List<ConnectionViewModel>();
            Parallel.ForEach(Connections, connection =>
            {
                if ((connection.SourceNode == node || connection.TargetNode == node) && !connections.Contains(connection))
                {
                    connections.Add(connection);
                }
            });
            return connections;
        }

        #endregion

        #region Getting Nodes

        /// <summary>
        /// Get a node from an Object
        /// </summary>
        /// <param name="nodeObj"></param>
        /// <returns></returns>
        public NodeBaseModel GetNode(object nodeObj)
        {
            NodeBaseModel got = null;
            Parallel.ForEach(Nodes, (node, state) =>
            {
                if (node.Equals(nodeObj))
                {
                    got = node;
                    state.Break();
                }
            });
            return got;
        }
        
        /// <summary>
        /// Get a node from a guid
        /// </summary>
        /// <param name="nodeGuid"></param>
        /// <returns></returns>
        public NodeBaseModel GetNode(AssetClassGuid nodeGuid)
        {
            NodeBaseModel got = null;
            Parallel.ForEach(Nodes, (node, state) =>
            {
                if (node.Guid == nodeGuid)
                {
                    got = node;
                    state.Break();
                }
            });
            return got;
        }

        /// <summary>
        /// Gets an interface node(s) from its name. Normally there can only be 1 of these, but Fields can be both Source and Target, meaning 2 can have the same name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns>A list of all of the nodes found</returns>
        public List<InterfaceDataNode> GetNode(string name)
        {
            List<InterfaceDataNode> nodes = new List<InterfaceDataNode>();
            if (InterfaceInputDataNodes.ContainsKey(name))
            {
                nodes.Add(InterfaceInputDataNodes[name]);
            }

            if (InterfaceOutputDataNodes.ContainsKey(name))
            {
                nodes.Add(InterfaceOutputDataNodes[name]);
            }

            return nodes;
        }

        #endregion
    }
    
    #endregion

    #region Pending Connection

    /// <summary>
    /// This executes <see cref="StartCommand"/> when we first drag an output
    /// Then executes <see cref="FinishCommand"/> when we let go of the output
    /// </summary>
    public class PendingConnectionViewModel
    {
        public OutputViewModel Source { get; set; }
        public InputViewModel Target { get; set; }

        public ICommand StartCommand { get; }
        public ICommand FinishCommand { get; }
        
        public Point SourceAnchor { get; set; }
        public Point TargetAnchor { get; set; }
        
        public PendingConnectionViewModel(EditorViewModel nodeEditor, EbxBaseEditor ebxEditor)
        {
            StartCommand = new DelegateCommand<Object>(source =>
            {
                //Open the asset when editing in order to ensure the least issues
                App.EditorWindow.OpenAsset(App.AssetManager.GetEbxEntry(nodeEditor.EditedEbxAsset.FileGuid));
                if (source.GetType().Name == "OutputViewModel")
                {
                    Source = (OutputViewModel)source;
                }
                else
                {
                    Target = (InputViewModel)source;
                }
            });
            FinishCommand = new DelegateCommand<Object>(target =>
            {
                ConnectionViewModel connection = null;
                if (target != null && target.GetType().Name != "OutputViewModel" && Source != null && Source.Type == ((InputViewModel)target).Type)
                {
                    connection = nodeEditor.Connect(Source, (InputViewModel)target);
                }
                else if (target != null && target.GetType().Name == "OutputViewModel" && Target != null && Target.Type == ((OutputViewModel)target).Type)
                {
                    connection = nodeEditor.Connect((OutputViewModel)target, Target);
                }
                Source = null; //Set these values to null that way they aren't saved in memory
                Target = null;

                #region Edit Ebx
                
                ebxEditor.CreateConnectionObject(connection);

                App.AssetManager.ModifyEbx(App.AssetManager.GetEbxEntry(nodeEditor.EditedEbxAsset.FileGuid).Name, nodeEditor.EditedEbxAsset);
                App.EditorWindow.DataExplorer.RefreshItems();

                #endregion
            });
        }
    }

    #endregion
}