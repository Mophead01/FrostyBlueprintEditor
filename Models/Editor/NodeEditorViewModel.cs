﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BlueprintEditor.Models.Connections;
using BlueprintEditor.Models.Types;
using BlueprintEditor.Models.Types.Shared;
using BlueprintEditor.Utils;
using Frosty.Core;
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
        
        public Dictionary<string, InterfaceDataNode> InterfaceInputDataNodes = new Dictionary<string, InterfaceDataNode>();
        public Dictionary<string, InterfaceDataNode> InterfaceOutputDataNodes = new Dictionary<string, InterfaceDataNode>();
        public ObservableCollection<NodeBaseModel> SelectedNodes { get; } = new ObservableCollection<NodeBaseModel>();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new ObservableCollection<ConnectionViewModel>();
        public PendingConnectionViewModel PendingConnection { get; }
        public SolidColorBrush PendingConnectionColor { get; } = new SolidColorBrush(Colors.White);
        public ICommand DisconnectConnectorCommand { get; }

        public string EditorName { get; }
        public EbxAsset EditedEbxAsset { get; set; }
        public AssetClassGuid InterfaceGuid { get; set; }
        public dynamic EditedProperties => EditedEbxAsset.RootObject;

        public EditorViewModel()
        {
            EditedEbxAsset = App.AssetManager.GetEbx((EbxAssetEntry)App.EditorWindow.GetOpenedAssetEntry());
            EditorName = App.EditorWindow.GetOpenedAssetEntry().Filename;
            
            PendingConnection = new PendingConnectionViewModel(this);
            
            DisconnectConnectorCommand = new DelegateCommand<Object>(connector =>
            {
                //ConnectionViewModel connection = connector.GetType().Name == "InputViewModel" ? Connections.First(x => x.Target == connector) : Connections.First(x => x.Source == connector);
                if (connector.GetType().Name == "InputViewModel")
                {
                    Disconnect(Connections.First(x => x.Target == connector));
                }
                else
                {
                    Disconnect(Connections.First(x => x.Source == connector));
                }
            });

            EditorUtils.Editors.Add(EditorName, this);
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
            NodeBaseModel newNode = null;
            
            if (NodeUtils.NodeExtensions.ContainsKey(key))
            {
                newNode = (NodeBaseModel)Activator.CreateInstance(NodeUtils.NodeExtensions[key].GetType());
            }
            else if (NodeUtils.NmcExtensions.ContainsKey(key))
            {
                newNode = new NodeBaseModel();
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
            else //We could not find an extension
            {
                newNode = new NodeBaseModel();
                newNode.Inputs = NodeUtils.GenerateNodeInputs(obj.GetType(), newNode);
                newNode.Name = obj.GetType().Name;
            }
            
            newNode.Object = obj;
            newNode.OnCreation();

            EditorUtils.CurrentEditor.Nodes.Add(newNode);
            return newNode;
        }

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
                    
                    foreach (ConnectionViewModel connection in EditorUtils.CurrentEditor.GetConnections(input))
                    {
                        EditorUtils.CurrentEditor.Disconnect(connection);
                    }
                    
                    switch (input.Type)
                    {
                        case ConnectionType.Property:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.Fields)
                            {
                                if (field.Name.ToString() == input.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.Fields.Remove(objToRemove);
                            }
                            break;
                        }
                        case ConnectionType.Event:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.OutputEvents)
                            {
                                if (field.Name.ToString() == input.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.OutputEvents.Remove(objToRemove);
                            }
                            break;
                        }
                        case ConnectionType.Link:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.OutputLinks)
                            {
                                if (field.Name.ToString() == input.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.OutputLinks.Remove(objToRemove);
                            }
                            break;
                        }
                    }
                }
                
                else
                {
                    var output = node.Outputs[0];
                    
                    foreach (ConnectionViewModel connection in EditorUtils.CurrentEditor.GetConnections(output))
                    {
                        EditorUtils.CurrentEditor.Disconnect(connection);
                    }
                    
                    switch (output.Type)
                    {
                        case ConnectionType.Property:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.Fields)
                            {
                                if (field.Name.ToString() == output.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.Fields.Remove(objToRemove);
                            }
                            break;
                        }
                        case ConnectionType.Event:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.InputEvents)
                            {
                                if (field.Name.ToString() == output.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.InputEvents.Remove(objToRemove);
                            }
                            break;
                        }
                        case ConnectionType.Link:
                        {
                            dynamic objToRemove = null;
                            foreach (dynamic field in EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.InputLinks)
                            {
                                if (field.Name.ToString() == output.Title)
                                {
                                    objToRemove = field;
                                }
                            }

                            if (objToRemove != null)
                            {
                                EditorUtils.CurrentEditor.EditedProperties.Interface.Internal.InputLinks.Remove(objToRemove);
                            }
                            break;
                        }
                    }
                }

                EditorUtils.CurrentEditor.Nodes.Remove(node);
                
                App.AssetManager.ModifyEbx(App.AssetManager.GetEbxEntry(EditorUtils.CurrentEditor.EditedEbxAsset.FileGuid).Name, EditorUtils.CurrentEditor.EditedEbxAsset);
                App.EditorWindow.DataExplorer.RefreshItems();
                return;
            }

            #endregion

            #region Object Removal
            
            foreach (ConnectionViewModel connection in EditorUtils.CurrentEditor.GetConnections(node))
            {
                EditorUtils.CurrentEditor.Disconnect(connection);
            }

            //Remove the object pointer
            List<PointerRef> pointerRefs = EditorUtils.CurrentEditor.EditedProperties.Objects;
            pointerRefs.RemoveAll(pointer => ((dynamic)pointer.Internal).GetInstanceGuid() == node.Object.GetInstanceGuid());
            
            EditorUtils.CurrentEditor.EditedEbxAsset.RemoveObject(node.Object);
            EditorUtils.CurrentEditor.Nodes.Remove(node);
            
            App.AssetManager.ModifyEbx(App.AssetManager.GetEbxEntry(EditorUtils.CurrentEditor.EditedEbxAsset.FileGuid).Name, EditorUtils.CurrentEditor.EditedEbxAsset);
            App.EditorWindow.DataExplorer.RefreshItems();

            #endregion
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
                    NodeBaseModel node = InterfaceDataNode.CreateInterfaceDataNode(field, false, InterfaceGuid);
                    node.Object = obj;
                    EditorUtils.CurrentEditor.Nodes.Add(node);
                }
                else if (field.AccessType.ToString() == "FieldAccessType_Target") //Target
                {
                    NodeBaseModel node = InterfaceDataNode.CreateInterfaceDataNode(field, true, InterfaceGuid);
                    node.Object = obj;
                    EditorUtils.CurrentEditor.Nodes.Add(node);
                }
                else //Source and Target
                {
                    NodeBaseModel node = InterfaceDataNode.CreateInterfaceDataNode(field, true, InterfaceGuid);
                    node.Object = obj;
                    EditorUtils.CurrentEditor.Nodes.Add(node);
                    
                    node = InterfaceDataNode.CreateInterfaceDataNode(field, false, InterfaceGuid);
                    node.Object = obj;
                    EditorUtils.CurrentEditor.Nodes.Add(node);
                }
            }

            foreach (dynamic inputEvent in ((dynamic)obj).InputEvents)
            {
                NodeBaseModel node = InterfaceDataNode.CreateInterfaceDataNode(inputEvent, true, InterfaceGuid);
                node.Object = obj;
                EditorUtils.CurrentEditor.Nodes.Add(node);
            }
                
            foreach (dynamic outputEvent in ((dynamic)obj).OutputEvents)
            {
                NodeBaseModel node = InterfaceDataNode.CreateInterfaceDataNode(outputEvent, false, InterfaceGuid);
                node.Object = obj;
                EditorUtils.CurrentEditor.Nodes.Add(node);
            }
                
            foreach (dynamic inputLink in ((dynamic)obj).InputLinks)
            {
                NodeBaseModel node = InterfaceDataNode.CreateInterfaceDataNode(inputLink, true, InterfaceGuid);
                node.Object = obj;
                EditorUtils.CurrentEditor.Nodes.Add(node);
            }
                
            foreach (dynamic outputLink in ((dynamic)obj).OutputLinks)
            {
                NodeBaseModel node = InterfaceDataNode.CreateInterfaceDataNode(outputLink, false, InterfaceGuid);
                node.Object = obj;
                EditorUtils.CurrentEditor.Nodes.Add(node);
            }
        }

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
            
            //TODO: This code sucks! Please find a faster way to find the connection and remove it
            switch (connection.Type)
            {
                case ConnectionType.Event:
                {
                    foreach (dynamic eventConnection in EditedProperties.EventConnections)
                    {
                        if (!connection.Equals(eventConnection)) continue;
                        EditedProperties.EventConnections.Remove(eventConnection);
                        break;
                    }
                    break;
                }
                case ConnectionType.Property:
                {
                    foreach (dynamic propertyConnection in EditedProperties.PropertyConnections)
                    {
                        if (!connection.Equals(propertyConnection)) continue;
                        EditedProperties.PropertyConnections.Remove(propertyConnection);
                        break;
                    }
                    break;
                }
                case ConnectionType.Link:
                {
                    foreach (dynamic linkConnection in EditedProperties.LinkConnections)
                    {
                        if (!connection.Equals(linkConnection)) continue;
                        EditedProperties.LinkConnections.Remove(linkConnection);
                        break;
                    }
                    break;
                }
            }
            
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
        
        public PendingConnectionViewModel(EditorViewModel editor)
        {
            StartCommand = new DelegateCommand<Object>(source =>
            {
                //Open the asset when editing in order to ensure the least issues
                App.EditorWindow.OpenAsset(App.AssetManager.GetEbxEntry(editor.EditedEbxAsset.FileGuid));
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
                    connection = editor.Connect(Source, (InputViewModel)target);
                }
                else if (target != null && target.GetType().Name == "OutputViewModel" && Target != null && Target.Type == ((OutputViewModel)target).Type)
                {
                    connection = editor.Connect((OutputViewModel)target, Target);
                }
                Source = null; //Set these values to null that way they aren't saved in memory
                Target = null;

                #region Edit Ebx

                if (connection != null)
                    switch (connection.Type)
                    {
                        case ConnectionType.Event:
                        {
                            dynamic eventConnection = TypeLibrary.CreateObject("EventConnection");

                            eventConnection.Source = new PointerRef(connection.SourceNode.Object);
                            eventConnection.Target = new PointerRef(connection.TargetNode.Object);
                            eventConnection.SourceEvent.Name = connection.SourceField;
                            eventConnection.TargetEvent.Name = connection.TargetField;

                            ((dynamic)editor.EditedEbxAsset.RootObject).EventConnections
                                .Add(eventConnection);
                            connection.Object = eventConnection;
                            break;
                        }
                        case ConnectionType.Property:
                        {
                            dynamic propertyConnection = TypeLibrary.CreateObject("PropertyConnection");

                            propertyConnection.Source = new PointerRef(connection.SourceNode.Object);
                            propertyConnection.Target = new PointerRef(connection.TargetNode.Object);
                            propertyConnection.SourceField = connection.SourceField;
                            propertyConnection.TargetField = connection.TargetField;

                            ((dynamic)editor.EditedEbxAsset.RootObject).PropertyConnections
                                .Add(propertyConnection);
                            connection.Object = propertyConnection;

                            break;
                        }
                        case ConnectionType.Link:
                        {
                            dynamic linkConnection = TypeLibrary.CreateObject("LinkConnection");

                            linkConnection.Source = new PointerRef(connection.SourceNode.Object);
                            linkConnection.Target = new PointerRef(connection.TargetNode.Object);
                            linkConnection.SourceField = connection.SourceField;
                            linkConnection.TargetField = connection.TargetField;

                            ((dynamic)editor.EditedEbxAsset.RootObject).LinkConnections.Add(
                                linkConnection);
                            connection.Object = linkConnection;

                            break;
                        }
                    }

                App.AssetManager.ModifyEbx(App.AssetManager.GetEbxEntry(editor.EditedEbxAsset.FileGuid).Name, editor.EditedEbxAsset);
                App.EditorWindow.DataExplorer.RefreshItems();

                #endregion
            });
        }
    }

    #endregion
}