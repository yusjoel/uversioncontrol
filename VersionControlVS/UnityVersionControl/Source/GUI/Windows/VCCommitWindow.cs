// Copyright (c) <2012> <Playdead>
// This file is subject to the MIT License as seen in the trunk of this repository
// Maintained by: <Kristian Kjems> <kristian.kjems+UnityVC@gmail.com>
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using MultiColumnState = MultiColumnState<string, UnityEngine.GUIContent>;

namespace VersionControl.UserInterface
{
    internal class VCCommitWindow : EditorWindow
    {
        // Const
        const float minimumControlHeight = 50;

        // State
        public IEnumerable<string> commitedFiles = new List<string>();
        
        private IEnumerable<string> assetPaths = new List<string>();
        private IEnumerable<string> depedencyAssetPaths = new List<string>();
        private bool firstTime = true;
        private bool commitInProgress = false;
        private bool commitCompleted = false;
        private string commitProgress = "";
        private float commitMessageHeight;
        private string commitMessage = null;
        private string CommitMessage
        {
            get { return commitMessage ?? (commitMessage = EditorPrefs.GetString("VCCommitWindow/CommitMessage", "")); }
            set { commitMessage = value; EditorPrefs.SetString("VCCommitWindow/CommitMessage", commitMessage); }
        }
        
        // Cache
        private Vector2 scrollViewVectorLog = Vector2.zero;
        private Vector2 statusScroll = Vector2.zero;
        private Rect rect;
        
        VCMultiColumnAssetList vcMultiColumnAssetList;

        public static void Init()
        {
            GetWindow<VCCommitWindow>("Commit");
        }

        public void SetAssetPaths(IEnumerable<string> assets, IEnumerable<string> dependencies)
        {
            Profiler.BeginSample("CommitWindow::SetAssetPaths");
            assetPaths = assets.ToList();
            depedencyAssetPaths = dependencies.ToList();
            vcMultiColumnAssetList.SetBaseFilter(BaseFilter);
            vcMultiColumnAssetList.ForEachRow(r => r.selected = VCSettings.IncludeDepedenciesAsDefault || assetPaths.Contains(r.data.assetPath));
            Profiler.EndSample();
        }

        private bool BaseFilter(VersionControlStatus vcStatus)
        {
            using (PushStateUtility.Profiler("CommitWindow::BaseFilter"))
            {
                string key = vcStatus.assetPath;
                key = key.EndsWith(VCCAddMetaFiles.meta) ? key.Remove(key.Length - VCCAddMetaFiles.meta.Length) : key;
                var metaStatus = vcStatus.MetaStatus();
                bool interresting = (vcStatus.fileStatus != VCFileStatus.None &&
                                    (vcStatus.fileStatus != VCFileStatus.Normal || (metaStatus != null && metaStatus.fileStatus != VCFileStatus.Normal))) ||
                                    vcStatus.lockStatus == VCLockStatus.LockedHere;

                if (!interresting) return false;
                return (assetPaths.Contains(key, System.StringComparer.InvariantCultureIgnoreCase) || depedencyAssetPaths.Contains(key, System.StringComparer.InvariantCultureIgnoreCase));
            }
        }

        private void UpdateFilteringOfKeys()
        {
            vcMultiColumnAssetList.RefreshGUIFilter();
        }

        private void StatusCompleted()
        {
            vcMultiColumnAssetList.ForEachRow(r => r.selected = VCSettings.IncludeDepedenciesAsDefault || assetPaths.Contains(r.data.assetPath));
            Repaint();
        }

        private void OnEnable()
        {
            minSize = new Vector2(250,100);
            commitMessageHeight = EditorPrefs.GetFloat("VCCommitWindow/commitMessageHeight", 1000.0f);
            rect = new Rect(0, commitMessageHeight, position.width, 10.0f);
            vcMultiColumnAssetList = new VCMultiColumnAssetList();
            UpdateFilteringOfKeys();
            VCCommands.Instance.StatusCompleted += StatusCompleted;
        }
        
        private void OnDisable()
        {
            EditorPrefs.SetFloat("VCCommitWindow/commitMessageHeight", commitMessageHeight);
            vcMultiColumnAssetList.Dispose();
        }
        
        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();
            if (commitInProgress) CommitProgressGUI();
            else CommitMessageGUI();
            EditorGUILayout.EndVertical();
        }

        private void CommitProgressGUI()
        {
            scrollViewVectorLog = EditorGUILayout.BeginScrollView(scrollViewVectorLog, false, false);
            GUILayout.TextArea(commitProgress);
            EditorGUILayout.EndScrollView();
            if (commitCompleted)
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close"))
                {
                    Close();
                }
            }
        }

        private void CommitMessageGUI()
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
            rect = GUIControls.DragButton(rect, GUIContent.none, null);
            rect.x = 0.0f;
            rect.width = position.width;
            commitMessageHeight = rect.y = Mathf.Clamp(rect.y, minimumControlHeight, position.height - minimumControlHeight);

            GUILayout.BeginArea(new Rect(0, 0, position.width, rect.y));
            vcMultiColumnAssetList.DrawGUI();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(0, rect.y, position.width, position.height - rect.y));
            DrawButtons();
            GUILayout.EndArea();
        }

        private void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.SetNextControlName("CommitMessage");
            using (GUILayoutHelper.BackgroundColor(CommitMessage.Length < 10 ? new Color(1, 0, 0) : new Color(0, 1, 0)))
            {
                statusScroll = EditorGUILayout.BeginScrollView(statusScroll, false, false);
                CommitMessage = EditorGUILayout.TextArea(CommitMessage, GUILayout.MinWidth(100), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
            if (firstTime)
            {
                GUI.FocusControl("CommitMessage");
                firstTime = false;
            }

            using (new PushState<bool>(GUI.enabled, VCCommands.Instance.Ready, v => GUI.enabled = v))
            {
                if (GUILayout.Button(Terminology.commit, GUILayout.Width(100)))
                {
                    if (vcMultiColumnAssetList.GetSelectedAssets().Count() != 0)
                    {
                        VCCommands.Instance.ProgressInformation += s =>
                        {
                            commitProgress = s + "\n" + commitProgress;
                            Repaint();
                        };
                        var commitTask = VCCommands.Instance.CommitTask(vcMultiColumnAssetList.GetSelectedAssets().ToList(), CommitMessage);
                        commitTask.ContinueWithOnNextUpdate(result =>
                        {
                            if (result)
                            {
                                commitedFiles = vcMultiColumnAssetList.GetSelectedAssets();
                                CommitMessage = "";
                                Repaint();
                                if(VCSettings.AutoCloseAfterSuccess) Close();
                            }
                            commitCompleted = true;
                        });
                        commitInProgress = true;
                    }
                    else
                    {
                        ShowNotification(new GUIContent("No files selected"));
                    }
                }
                if (GUILayout.Button("Cancel", GUILayout.Width(100)))
                {
                    Close();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (vcMultiColumnAssetList.GetSelectedAssets().Any())
            {
                RemoveNotification();
            }
        }
    }
}

