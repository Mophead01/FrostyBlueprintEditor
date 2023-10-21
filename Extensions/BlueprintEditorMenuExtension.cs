﻿using System;
using System.Windows.Media;
using BlueprintEditor.Utils;
using BlueprintEditor.Windows;
using Frosty.Core;

namespace BlueprintEditor.Extensions
{
    public class ViewBlueprintMenuExtension : MenuExtension
    {
        public static ImageSource iconImageSource = new ImageSourceConverter().ConvertFromString("pack://application:,,,/BlueprintEditor;component/Images/BlueprintEdit.png") as ImageSource;

        public override string TopLevelMenuName => "View";
        public override string SubLevelMenuName => null;

        public override string MenuItemName => "Blueprint Editor";
        public override ImageSource Icon => iconImageSource;

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            if (App.EditorWindow.GetOpenedAssetEntry() != null && !EditorUtils.Editors.ContainsKey(App.EditorWindow.GetOpenedAssetEntry().Filename))
            {
                BlueprintEditorWindow blueprintEditor = new BlueprintEditorWindow();
                blueprintEditor.Show();
                blueprintEditor.Initiate();

            }
            else if (App.EditorWindow.GetOpenedAssetEntry() == null)
            {
                App.Logger.LogError("Please open a blueprint(an asset with Property, Link, and Event connections, as well as Objects).");
            }
            else if (EditorUtils.Editors.ContainsKey(App.EditorWindow.GetOpenedAssetEntry().Filename))
            {
                App.Logger.LogError("This editor is already open.");
            }
        });
    }
}