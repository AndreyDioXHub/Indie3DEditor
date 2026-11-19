using UnityEditor;
using UnityEngine;

namespace Indie3DEditor.EditMesh
{
	public static class IndieEditMeshTransformTool
	{
		private const float RotationChangeDotEpsilon = 0.999999f;

		private static bool _dragging;
		private static bool _shiftExtrude;
		private static bool _shiftExtrudeEdge;
		private static bool _shiftExtrudeProxy;
		private static int _shiftExtrudeNewFaceStart;
		private static int[] _affectedVertices;
		private static Vector3[] _originalPositions;
		private static Vector3 _startLocalPivot;
		private static Vector3 _startWorldPivot;
		private static Quaternion _handleRotation = Quaternion.identity;
		private static Vector3 _handleScale = Vector3.one;

		public static bool IsDragging => _dragging;

		/// <summary>Vertex set captured at drag start; null when not dragging.</summary>
		public static int[] DraggedVertices => _dragging ? _affectedVertices : null;

		public static Quaternion HandleRotation => _handleRotation;

		public static void ResetDrag()
		{
			if (_shiftExtrudeProxy)
				IndieEditMeshEditProxy.Cancel();

			_dragging = false;
			_shiftExtrude = false;
			_shiftExtrudeEdge = false;
			_shiftExtrudeProxy = false;
			_shiftExtrudeNewFaceStart = 0;
			_affectedVertices = null;
			_originalPositions = null;
			_startLocalPivot = Vector3.zero;
			_startWorldPivot = Vector3.zero;
			_handleRotation = Quaternion.identity;
			_handleScale = Vector3.one;
		}

		public static void Draw(IndieEditMeshComponent ep)
		{
			if (ep == null || ep.Data == null)
				return;

			// A settings-window preview owns the proxy; moving ep.Data under it would be lost.
			if (!_dragging && IndieEditMeshEditProxy.IsActiveFor(ep))
				return;

			var data = IndieEditMeshEditProxy.DataFor(ep);

			// The dragged set is fixed at BeginDrag; reuse it instead of re-expanding
			// Link Coincident over the whole mesh on every drag event.
			int[] currentAffected = DraggedVertices ??
			                        IndieEditMeshSelection.GetAffectedVertices(data, ep.subMode);

			if (currentAffected.Length == 0)
			{
				ResetDrag();
				return;
			}

			Vector3 localPivot = GetLocalPivot(data, currentAffected);
			Vector3 worldPivot = ep.transform.TransformPoint(localPivot);
			Vector3 handlePivot = worldPivot;

			if (!_dragging)
			{
				_handleRotation = Quaternion.identity;
				_handleScale = Vector3.one;
			}

			EditorGUI.BeginChangeCheck();

			switch (Tools.current)
			{
				case Tool.Move:
					handlePivot = Handles.PositionHandle(handlePivot, Quaternion.identity);
					break;

				case Tool.Rotate:
					// Must feed last value back; identity every frame causes micro-jitter.
					_handleRotation = Handles.RotationHandle(_handleRotation, worldPivot);
					break;

				case Tool.Scale:
					_handleScale = Handles.ScaleHandle(
						_handleScale,
						worldPivot,
						Quaternion.identity,
						HandleUtility.GetHandleSize(worldPivot)
					);
					break;
			}

			bool changed = EditorGUI.EndChangeCheck();

			if (Tools.current == Tool.Rotate &&
			    changed &&
			    !_dragging &&
			    !IsMeaningfulRotation(_handleRotation))
			{
				_handleRotation = Quaternion.identity;
				changed = false;
			}

			if (changed)
			{
				if (!_dragging)
				{
					if (ShouldBeginShiftExtrude(ep))
						BeginShiftExtrude(ep, worldPivot);
					else
						BeginDrag(ep, currentAffected, localPivot, worldPivot);
				}

				ApplyDrag(ep, handlePivot, _handleRotation, _handleScale);
				IndieEditMeshTransformTypeInWindow.NotifyMeshTransformed(false);
			}

			if (_dragging && ShouldEndDrag(changed))
				EndDrag(ep);
		}

		private static bool ShouldEndDrag(bool changedThisEvent)
		{
			Event evt = Event.current;

			if (evt == null)
				return false;

			// Handles call Use() on MouseUp, so .type becomes Used — rawType still reports it.
			if (evt.rawType == EventType.MouseUp)
				return true;

			// Release missed (e.g. button let go outside the Scene view): the handle
			// no longer owns the mouse. Leaving the window mid-drag keeps hotControl,
			// so the drag is not cut short and Shift does not start a second extrude.
			return !changedThisEvent &&
			       GUIUtility.hotControl == 0 &&
			       evt.rawType != EventType.MouseDrag;
		}

		private static void EndDrag(IndieEditMeshComponent ep)
		{
			Quaternion committedRotation = _handleRotation;
			bool wasShiftExtrude = _shiftExtrude;
			bool wasShiftEdge = _shiftExtrudeEdge;
			int newFaceStart = _shiftExtrudeNewFaceStart;

			if (wasShiftEdge && ep.Data != null)
				IndieEditMeshEdgeOps.MatchNewFaceNormalsToAdjacent(ep.Data, newFaceStart);

			// Drag only streamed positions; rebuild normals / tangents / SG shading once.
			if (!_shiftExtrudeProxy || !IndieEditMeshEditProxy.Commit())
				IndieEditMeshSession.RefreshMesh(ep);

			_shiftExtrudeProxy = false;
			ResetDrag();
			IndieEditMeshTransformTypeInWindow.NotifyMeshTransformed(
				true,
				wasShiftExtrude ? default : committedRotation
			);

			if (wasShiftExtrude)
				IndieEditMeshTransformTypeInWindow.NotifySelectionOrMeshChanged();
		}

		private static bool ShouldBeginShiftExtrude(IndieEditMeshComponent ep)
		{
			if (Tools.current != Tool.Move)
				return false;

			if (IndieEditMeshPreviewOp.IsActive)
				return false;

			if (ep.Data == null)
				return false;

			Event evt = Event.current;

			if (evt == null || !evt.shift)
				return false;

			if (ep.subMode == IndieEditMeshSubMode.Polygon)
				return ep.Data.selectedFaces.Count > 0;

			if (ep.subMode == IndieEditMeshSubMode.Edge)
				return ep.Data.selectedEdges.Count > 0;

			return false;
		}

		private static bool IsMeaningfulRotation(Quaternion rotation)
		{
			return Mathf.Abs(Quaternion.Dot(rotation, Quaternion.identity)) < RotationChangeDotEpsilon;
		}

		private static Vector3 GetLocalPivot(IndieEditableMeshData data, int[] affectedVertices)
		{
			Vector3 pivot = Vector3.zero;

			if (affectedVertices.Length == 0)
				return pivot;

			foreach (int index in affectedVertices)
			{
				pivot += data.vertices[index];
			}

			pivot /= affectedVertices.Length;

			return pivot;
		}

		private static void BeginShiftExtrude(IndieEditMeshComponent ep, Vector3 worldPivot)
		{
			IndieEditMeshSubMode dragMode = ep.subMode;
			_shiftExtrudeEdge = false;
			_shiftExtrudeNewFaceStart = ep.Data.faces.Count;

			if (dragMode == IndieEditMeshSubMode.Edge)
			{
				IndieEditMeshUndo.Register(ep, "Extrude Edges");
				// Height 0: bridge quads between original and new edge; handle moves the new edge.
				_shiftExtrudeNewFaceStart = IndieEditMeshEdgeOps.ApplyShiftExtrude(ep.Data);
				_shiftExtrudeEdge = true;
			}
			else
			{
				IndieEditMeshUndo.Register(ep, "Extrude Polygons");

				// The drag runs on the selection region only; EndDrag merges it once.
				_shiftExtrudeProxy = IndieEditMeshEditProxy.Begin(ep);

				// Topology only: move caps later with the handle.
				IndieEditMeshFaceOps.ApplyExtrude(
					IndieEditMeshEditProxy.DataFor(ep),
					0f,
					IndieEditMeshFaceParams.ExtrudeType
				);

				if (_shiftExtrudeProxy)
					IndieEditMeshEditProxy.RefreshMesh();

				dragMode = IndieEditMeshSubMode.Polygon;
			}

			var data = IndieEditMeshEditProxy.DataFor(ep);

			// Caps/new edges sit on the old positions at height 0 — do not Link Coincident
			// or the base stack moves with the cap and extrude looks like a plain drag.
			int[] affected = IndieEditMeshSelection.GetAffectedVertices(
				data,
				dragMode,
				expandCoincident: false
			);

			_dragging = true;
			_shiftExtrude = true;
			_affectedVertices = new int[affected.Length];
			_originalPositions = new Vector3[affected.Length];

			for (int i = 0; i < affected.Length; i++)
			{
				int vertexIndex = affected[i];
				_affectedVertices[i] = vertexIndex;
				_originalPositions[i] = data.vertices[vertexIndex];
			}

			_startLocalPivot = GetLocalPivot(data, affected);
			_startWorldPivot = worldPivot;
		}

		private static void BeginDrag(
			IndieEditMeshComponent ep,
			int[] affectedVertices,
			Vector3 localPivot,
			Vector3 worldPivot)
		{
			IndieEditMeshUndo.Register(ep, "Edit Mesh Transform");

			_dragging = true;
			_shiftExtrude = false;
			_affectedVertices = new int[affectedVertices.Length];
			_originalPositions = new Vector3[affectedVertices.Length];

			for (int i = 0; i < affectedVertices.Length; i++)
			{
				int vertexIndex = affectedVertices[i];

				_affectedVertices[i] = vertexIndex;
				_originalPositions[i] = ep.Data.vertices[vertexIndex];
			}

			_startLocalPivot = localPivot;
			_startWorldPivot = worldPivot;
		}

		private static void ApplyDrag(
			IndieEditMeshComponent ep,
			Vector3 newWorldPivot,
			Quaternion worldRotation,
			Vector3 newScale)
		{
			if (!_dragging || _affectedVertices == null || _originalPositions == null)
				return;

			if (_shiftExtrudeProxy && !IndieEditMeshEditProxy.IsActiveFor(ep))
			{
				ResetDrag();
				return;
			}

			var data = IndieEditMeshEditProxy.DataFor(ep);
			var transform = ep.transform;

			if (Tools.current == Tool.Move || _shiftExtrude)
			{
				Vector3 deltaWorld = newWorldPivot - _startWorldPivot;
				Vector3 deltaLocal = transform.InverseTransformVector(deltaWorld);

				for (int i = 0; i < _affectedVertices.Length; i++)
				{
					int vertexIndex = _affectedVertices[i];

					if (vertexIndex >= 0 && vertexIndex < data.vertices.Count)
					{
						data.vertices[vertexIndex] = _originalPositions[i] + deltaLocal;
					}
				}
			}
			else if (Tools.current == Tool.Rotate)
			{
				Quaternion localRotation =
					Quaternion.Inverse(transform.rotation) *
					worldRotation *
					transform.rotation;

				for (int i = 0; i < _affectedVertices.Length; i++)
				{
					int vertexIndex = _affectedVertices[i];

					if (vertexIndex >= 0 && vertexIndex < data.vertices.Count)
					{
						Vector3 offset = _originalPositions[i] - _startLocalPivot;
						data.vertices[vertexIndex] = _startLocalPivot + localRotation * offset;
					}
				}
			}
			else if (Tools.current == Tool.Scale)
			{
				for (int i = 0; i < _affectedVertices.Length; i++)
				{
					int vertexIndex = _affectedVertices[i];

					if (vertexIndex >= 0 && vertexIndex < data.vertices.Count)
					{
						Vector3 offset = _originalPositions[i] - _startLocalPivot;
						data.vertices[vertexIndex] = _startLocalPivot + Vector3.Scale(offset, newScale);
					}
				}
			}

			if (_shiftExtrudeEdge)
			{
				// Winding of the new strip can flip mid-drag — indices change, full rebuild.
				IndieEditMeshEdgeOps.MatchNewFaceNormalsToAdjacent(ep.Data, _shiftExtrudeNewFaceStart);
				IndieEditMeshSession.RefreshMesh(ep);
				return;
			}

			if (_shiftExtrudeProxy)
			{
				IndieEditMeshEditProxy.RefreshPositions();
				return;
			}

			IndieEditMeshSession.RefreshMeshPositions(ep);
		}
	}
}
