﻿using BlueprintEditorPlugin.Editors.BlueprintEditor.Connections;
using Frosty.Core.Controls;

namespace BlueprintEditorPlugin.Editors.BlueprintEditor.Nodes.TypeMapping
{
    public class EventSwitch : EntityNode
    {
        public override string ObjectType => "EventSwitchEntityData";
        public override string ToolTip => "A switch which changes what event it outputs depending on the current OutEvent";

        public override void OnCreation()
        {
            base.OnCreation();
            
            uint inCount = (uint)TryGetProperty("OutEvents");

            for (uint i = 0; i < inCount; i++)
            {
                AddOutput($"Out{i}", ConnectionType.Event, Realm);
            }
        }
        
        public override void OnObjectModified(object sender, ItemModifiedEventArgs args)
        {
            base.OnObjectModified(sender, args);

            if (args.Item.Name == "OutEvents")
            {
                uint oldCount = (uint)args.OldValue;
                uint outCount = (uint)TryGetProperty("OutEvents");

                if (outCount == 0)
                {
                    ClearOutputs();
                    return;
                }
                
                if (oldCount < outCount)
                {
                    // Add new inputs
                    for (uint i = 1; i <= outCount; i++)
                    {
                        if (GetOutput($"Out{i}", ConnectionType.Property) != null)
                            continue;
                        
                        AddOutput($"Out{i}", ConnectionType.Event, Realm);
                    }
                }
                else
                {
                    for (uint i = oldCount; i > 1; i--)
                    {
                        RemoveOutput($"Out{i}", ConnectionType.Event);
                    }
                }
            }
        }
    }
}