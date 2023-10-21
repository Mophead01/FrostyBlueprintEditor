﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using BlueprintEditor.Models.Connections;
using BlueprintEditor.Models.Types;
using BlueprintEditor.Models.Types.Shared;
using Frosty.Core;
using FrostySdk.Ebx;

namespace BlueprintEditor.Utils
{
    public static class NodeUtils
    {
        /// <summary>
        /// A list of extensions for nodes
        /// </summary>
        public static Dictionary<string, NodeBaseModel> NodeExtensions = new Dictionary<string, NodeBaseModel>();

        public static Dictionary<string, List<string>> NmcExtensions = new Dictionary<string, List<string>>(); 

        private static string[] DataTypes =
        {
            "EntityData",
            "ObjectData",
            "ComponentData",
            "DescriptorData",
            "Data"
        };

        /// <summary>
        /// Cleans the name of a type so its easier to read
        /// E.g BoolEntityData will be changed to Bool
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns>A name without Data type inside it</returns>
        public static string CleanNodeName(string typeName)
        {
            foreach (string dataType in DataTypes)
            {
                if (typeName.Contains(dataType))
                {
                    return typeName.Replace(dataType, "");
                }
            }

            return typeName;
        }

        /// <summary>
        /// This will generate probabilistically determined property inputs for a Node
        /// </summary>
        /// <param name="objectType">The type of the object we are generating off of</param>
        /// <param name="nodeBaseModel"></param>
        /// <returns>Inputs for a node</returns>
        public static ObservableCollection<InputViewModel> GenerateNodeInputs(Type objectType,
            NodeBaseModel nodeBaseModel)
        {
            ObservableCollection<InputViewModel> inputs = new ObservableCollection<InputViewModel>();
            foreach (PropertyInfo property in objectType.GetProperties())
            {
                if (property.Name == "Flags" 
                    || property.Name == "Realm" 
                    || property.Name == "__Id" 
                    || property.Name == "__InstanceGuid" 
                    || property.Name.Contains("RuntimeComponentCount") 
                    || property.Name.Contains("RuntimeTransformationCount")
                    || property.Name.Contains("Components")
                   ) continue;

                inputs.Add(new InputViewModel()
                {
                    Title = property.Name,
                    Type = ConnectionType.Property
                });
            }

            return inputs;
        }

        static NodeUtils()
        {
            NodeExtensions.Add("null", new NodeBaseModel());
            foreach (var type in Assembly.GetCallingAssembly().GetTypes())
            {
                if (type.IsSubclassOf(typeof(NodeBaseModel)))
                {
                    var extension = (NodeBaseModel)Activator.CreateInstance(type);
                    NodeExtensions.Add(extension.ObjectType, extension);
                }
            }
            
            //Check if the directory for nmc's exists
            if (!Directory.Exists($@"{AppDomain.CurrentDomain.BaseDirectory}BlueprintEditor\NodeMappings"))
            {
                Directory.CreateDirectory($@"{AppDomain.CurrentDomain.BaseDirectory}BlueprintEditor\NodeMappings");
            }

            //Read our xml-style NodeMappings
            foreach (string file in Directory.GetFiles($@"{AppDomain.CurrentDomain.BaseDirectory}BlueprintEditor\NodeMappings", "*.nmc", SearchOption.AllDirectories))
            {
                StreamReader sr = new StreamReader(@file);
                string type = null;
                List<string> args = new List<string>();
                
                string currentLine = sr.ReadLine();
                while (currentLine != null)
                {
                    switch (currentLine.Replace(" = ", "=").Split('=')[0])
                    {
                        case "Type":
                        {
                            type = currentLine.Replace(" = ", "=").Split('=')[1];
                        } break;
                        case "DisplayName":
                        {
                            args.Add(currentLine.Replace(" = ", "="));
                        } break;
                        case "InputEvent":
                        {
                            args.Add(currentLine.Replace(" = ", "="));
                        } break;
                        case "InputProperty":
                        {
                            args.Add(currentLine.Replace(" = ", "="));
                        } break;
                        case "InputLink":
                        {
                            args.Add(currentLine.Replace(" = ", "="));
                        } break;
                        case "OutputEvent":
                        {
                            args.Add(currentLine.Replace(" = ", "="));
                        } break;
                        case "OutputProperty":
                        {
                            args.Add(currentLine.Replace(" = ", "="));
                        } break;
                        case "OutputLink":
                        {
                            args.Add(currentLine.Replace(" = ", "="));
                        } break;
                        default:
                        {
                            App.Logger.LogError("{1} contains an invalid argument, {0}", currentLine, file);
                        } break;
                    }

                    if (type != null && !NmcExtensions.ContainsKey(type))
                    {
                        NmcExtensions.Add(type, args);
                    }

                    currentLine = sr.ReadLine();
                }
            }
        }
    }
}