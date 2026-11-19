using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
	[InitializeOnLoad]
	public static class IndieEditMeshManager
	{
		private static bool _restoringSelection;
		private static int _lastTypeInSignature;
		private static bool _lastTypeInHasSelection;
		private static double _lastUndoRedoTime;

		static IndieEditMeshManager()
		{
			SceneView.duringSceneGui += OnSceneGUI;
			Selection.selectionChanged += OnSelectionChanged;
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			EditorApplication.update += EnsureActiveObjectSelected;
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange state)
		{
			if (state == PlayModeStateChange.ExitingEditMode)
			{
				if (IndieEditMeshSession.Active != null)
				{
					IndieEditMeshSession.EndEditing(IndieEditMeshSession.Active, true);
				}
			}
		}

		private static void OnSelectionChanged()
		{
			EnsureActiveObjectSelected();
		}

		private static void EnsureActiveObjectSelected()
		{
			if (_restoringSelection)
				return;

			var active = IndieEditMeshSession.Active;

			if (active == null)
				return;

			if (Selection.Contains(active.gameObject) && Selection.objects.Length == 1)
				return;

			_restoringSelection = true;
			Selection.objects = new Object[]
			{
				active.gameObject
			};
			// Selection.objects records Unity undo — wipe it immediately.
			IndieEditMeshUndo.SuppressUnityUndo();
			_restoringSelection = false;
		}

		private static void OnSceneGUI(SceneView sceneView)
		{
			var active = IndieEditMeshSession.Active;

			if (active == null)
			{
				if (_lastTypeInHasSelection)
				{
					_lastTypeInHasSelection = false;
					_lastTypeInSignature = 0;
					IndieEditMeshTransformTypeInWindow.CloseIfOpen();
				}

				return;
			}

			Event evt = Event.current;

			if (evt == null)
				return;

			IndieEditMeshCameraOrbit.Handle(active, sceneView, evt);

			if (HandleEditHotkeys(active, sceneView, evt))
				return;

			int blockControl = GUIUtility.GetControlID(FocusType.Passive);

			if (evt.type == EventType.Layout)
			{
				HandleUtility.AddDefaultControl(blockControl);
			}

			IndieEditMeshSceneView.Draw(active, sceneView);
			SyncTransformTypeInWindow(active);

			if (IndieEditMeshAttachTool.IsActive)
			{
				if (IndieEditMeshAttachTool.HandleSceneGUI(active, sceneView, evt))
					return;

				if (evt.alt ||
				    evt.type == EventType.ScrollWheel ||
				    (evt.button == 2 &&
				     (evt.type == EventType.MouseDown ||
				      evt.type == EventType.MouseDrag ||
				      evt.type == EventType.MouseUp)))
				{
					return;
				}

				if (evt.button == 0 &&
				    (evt.type == EventType.MouseDown ||
				     evt.type == EventType.MouseDrag ||
				     evt.type == EventType.MouseUp))
				{
					evt.Use();
				}

				return;
			}

			if (IndieEditMeshCutTool.IsActive)
			{
				IndieEditMeshCutTool.Draw(active, sceneView);

				if (IndieEditMeshCutTool.HandleSceneGUI(active, sceneView, evt))
					return;

				if (evt.alt ||
				    evt.type == EventType.ScrollWheel ||
				    (evt.button == 2 &&
				     (evt.type == EventType.MouseDown ||
				      evt.type == EventType.MouseDrag ||
				      evt.type == EventType.MouseUp)))
				{
					return;
				}

				if (evt.button == 0 &&
				    (evt.type == EventType.MouseDown ||
				     evt.type == EventType.MouseDrag ||
				     evt.type == EventType.MouseUp))
				{
					evt.Use();
				}

				return;
			}

			if (IndieEditMeshPointToPoint.TryConsumeRightClick(evt))
			{
				sceneView.Repaint();
				return;
			}

			if (IsCameraControlEvent(evt))
				return;

			if (IndieEditMeshPointToPoint.IsActive)
			{
				sceneView.wantsMouseMove = true;

				if (IndieEditMeshPointToPoint.HandleSceneGUI(active, sceneView, evt, blockControl))
					sceneView.Repaint();

				return;
			}

			if (
				evt.type == EventType.MouseDown ||
				evt.type == EventType.MouseDrag ||
				evt.type == EventType.MouseUp ||
				evt.type == EventType.ContextClick
			)
			{
				bool ourControl =
					HandleUtility.nearestControl == blockControl ||
					GUIUtility.hotControl == blockControl ||
					IndieEditMeshSelection.IsMarqueeActive;

				if (ourControl && evt.button == 0 && !evt.alt)
				{
					switch (evt.type)
					{
						case EventType.MouseDown:
							GUIUtility.hotControl = blockControl;
							IndieEditMeshSelection.HandleMouseDown(active, evt.mousePosition, evt);
							evt.Use();
							sceneView.Repaint();
							break;

						case EventType.MouseDrag:
							if (GUIUtility.hotControl == blockControl)
							{
								IndieEditMeshSelection.HandleMouseDrag(active, evt.mousePosition, evt);
								evt.Use();
								sceneView.Repaint();
							}
							break;

						case EventType.MouseUp:
							if (GUIUtility.hotControl == blockControl)
							{
								IndieEditMeshSelection.HandleMouseUp(
									active,
									sceneView,
									evt.mousePosition,
									evt
								);
								GUIUtility.hotControl = 0;
								evt.Use();
								sceneView.Repaint();
							}
							break;
					}
				}
				else if (evt.type == EventType.ContextClick ||
				         (evt.type == EventType.MouseDown && evt.button != 1 && evt.button != 2))
				{
					if (HandleUtility.nearestControl == blockControl)
					{
						evt.Use();
						sceneView.Repaint();
					}
				}
			}
		}

		private static void SyncTransformTypeInWindow(IndieEditMeshComponent active)
		{
			bool hasSelection = IndieEditMeshTransformTypeIn.HasSelection(active);
			int signature = hasSelection
				? IndieEditMeshTransformTypeIn.GetSelectionSignature(active)
				: 0;

			if (hasSelection == _lastTypeInHasSelection &&
			    signature == _lastTypeInSignature)
			{
				return;
			}

			_lastTypeInHasSelection = hasSelection;
			_lastTypeInSignature = signature;
			IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
		}

		private static bool IsCameraControlEvent(Event evt)
		{
			if (evt.alt)
				return true;

			switch (evt.type)
			{
				case EventType.MouseDown:
				case EventType.MouseDrag:
				case EventType.MouseUp:
					return evt.button == 1 || evt.button == 2;

				case EventType.ScrollWheel:
					return true;

				default:
					return false;
			}
		}

		private static bool HandleEditHotkeys(IndieEditMeshComponent active, SceneView sceneView, Event evt)
		{
			if (evt.type == EventType.ValidateCommand)
			{
				switch (evt.commandName)
				{
					case "SelectAll":
					case "Undo":
					case "Redo":
						evt.Use();
						return true;
				}

				return false;
			}

			if (evt.type == EventType.ExecuteCommand)
			{
				switch (evt.commandName)
				{
					case "SelectAll":
						IndieEditMeshSelection.SelectAll(active);
						evt.Use();
						SceneView.RepaintAll();
						return true;

					case "Undo":
						PerformEditMeshUndo(active);
						evt.Use();
						return true;

					case "Redo":
						PerformEditMeshRedo(active);
						evt.Use();
						return true;
				}

				return false;
			}

			if (evt.type != EventType.KeyDown)
				return false;

			bool ctrl = evt.control || evt.command;

			if (!ctrl &&
			    evt.keyCode == KeyCode.Z &&
			    !evt.shift &&
			    !evt.alt &&
			    !EditorGUIUtility.editingTextField)
			{
				if (active.Data != null)
				{
					IndieEditMeshViewFrame.Focus(
						sceneView,
						active.transform,
						active.Data.vertices,
						IndieEditMeshSelection.GetAffectedVertices(active.Data, active.subMode));
				}

				evt.Use();
				return true;
			}

			if (!ctrl)
				return false;

			if (evt.keyCode == KeyCode.A && !evt.shift && !evt.alt)
			{
				IndieEditMeshSelection.SelectAll(active);
				evt.Use();
				SceneView.RepaintAll();
				return true;
			}

			if (evt.keyCode == KeyCode.Z && !evt.shift)
			{
				PerformEditMeshUndo(active);
				evt.Use();
				return true;
			}

			if (evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shift))
			{
				PerformEditMeshRedo(active);
				evt.Use();
				return true;
			}

			return false;
		}

		private static void PerformEditMeshUndo(IndieEditMeshComponent active)
		{
			if (!TryConsumeUndoRedoGesture())
			{
				IndieEditMeshUndo.SuppressUnityUndo();
				return;
			}

			IndieEditMeshUndo.Undo(active);
			EnsureActiveObjectSelected();
			SceneView.RepaintAll();
		}

		private static void PerformEditMeshRedo(IndieEditMeshComponent active)
		{
			if (!TryConsumeUndoRedoGesture())
			{
				IndieEditMeshUndo.SuppressUnityUndo();
				return;
			}

			IndieEditMeshUndo.Redo(active);
			EnsureActiveObjectSelected();
			SceneView.RepaintAll();
		}

		private static bool TryConsumeUndoRedoGesture()
		{
			double now = EditorApplication.timeSinceStartup;

			if (now - _lastUndoRedoTime < 0.05)
				return false;

			_lastUndoRedoTime = now;
			return true;
		}
	}
}
