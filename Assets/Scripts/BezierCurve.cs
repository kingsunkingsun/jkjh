﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[Serializable]
public class BezierCurve : MonoBehaviour
{
	[HideInInspector] [SerializeField] private List<BezierPoint> points = new List<BezierPoint> ();
	[SerializeField] public bool connectedCurve = true;
	[SerializeField] [Range (0.01f, 1.0f)] public float handleSize = 0.1f;
	[SerializeField] [Range (0.1f, 1.0f)] public float snapSize = 0.5f;
	[SerializeField] [Range (10, 100)] public int sampleRate = 30;
	[SerializeField] [Range (0.0f, 0.2f)] public float tangentLimit = 0.9f;
	[SerializeField] [Range (0.1f, 10.0f)] public float minDistance = 30.0f;

	private void OnEnable ()
	{
		for (int i = 0; i < points.Count; i++) {
			points [i].SetToCurve (this);
		}
	}

	public List<BezierPoint> GetAnchorPoints ()
	{
		return points;
	}

	public void AddAnchorPoint (BezierPoint bezierPoint, int index = 0)
	{
		if (!points.Contains (bezierPoint)) {
			bezierPoint.name = string.Format ("Point {0}", points.Count);
			bezierPoint.SetToCurve (this);

			if (index == 0) {
				points.Add (bezierPoint);
			} else {
				points.Insert (index, bezierPoint);
			}
		}
	}

	public void RemoveAnchorPoint (BezierPoint bezierPoint)
	{
		points.Remove (bezierPoint);
	}

	public void RemoveAllAnchorPoints ()
	{
		points.Clear ();
	}

	public Vector3 GetPoint (BezierPoint p0, BezierPoint p1, float t)
	{
		if (p0.pointType == BezierPointType.None && p1.pointType == BezierPointType.None) {
			return BezierCurveLinear (p0.GetPosition (), p1.GetPosition (), t);
		} else if (p0.pointType != BezierPointType.None && p1.pointType == BezierPointType.None) {
			return QuadraticCurve (p0.GetPosition (), p0.GetHandle2Position (), p1.GetPosition (), t);
		} else if (p1.pointType != BezierPointType.None && p0.pointType == BezierPointType.None) {
			return QuadraticCurve (p0.GetPosition (), p1.GetHandle1Position (), p1.GetPosition (), t);
		} else {
			return CubicCurve (p0.GetPosition (), p0.GetHandle2Position (), p1.GetHandle1Position (), p1.GetPosition (), t);
		}
	}

	public Vector3 GetPointAt (float t)
	{
		if (t <= 0 || points.Count == 1) {
			return points [0].GetPosition ();
		}
		if (t >= 1) {
			if (connectedCurve && points.Count > 1) {
				return points [0].GetPosition ();
			}
			return points [points.Count - 1].GetPosition ();
		}

		BezierPoint p0 = null;
		BezierPoint p1 = null;

		int loopCount = points.Count - 1;
		float curvePercent = 0;
		float totalPercent = 0;

		if (connectedCurve && points.Count > 1)
			loopCount++;

		for (int i = 0; i < loopCount; i++) {
			int i0 = i;
			int i1 = (i + 1) % points.Count;

			curvePercent = ApproximateLength (points [i0], points [i1], sampleRate) / GetLength ();

			if (totalPercent + curvePercent > t) {
				p0 = points [i0];
				p1 = points [i1];
				break;
			} else {
				totalPercent += curvePercent;
			}
		}

		t -= totalPercent;

		return GetPoint (p0, p1, t / curvePercent);
	}

	public List<CurvePoint> GetPoints (bool includeLastPoint = true, bool createDoublePointsOnSharpEdges = false)
	{
		List<CurvePoint> curvePoints = new List<CurvePoint> ();

		if (points.Count == 1) {
			curvePoints.Add (new CurvePoint (points [0].GetPosition (), Vector3.zero, Vector3.zero));
			return curvePoints;
		}

		int loopCount = points.Count - 1;
		if (connectedCurve && points.Count > 1)
			loopCount++;

		for (int i = 0; i < loopCount; i++) {
			int i0 = i;
			int i1 = (i + 1) % points.Count;
			int lc = loopCount - 1;
			bool isLastAnchorPoint = i == lc;

			BezierPoint p0 = points [i0];
			BezierPoint p1 = points [i1];

			for (int j = 0; j <= sampleRate; j++) {
				bool isLastPointOnCurvePart = j == sampleRate;
				bool c1 = isLastPointOnCurvePart && !isLastAnchorPoint;
				bool isLastPoint = isLastPointOnCurvePart && isLastAnchorPoint;
				bool c2 = c1 && p1.pointType != BezierPointType.Connected && createDoublePointsOnSharpEdges;

				if (!c2) {
					if (c1 || isLastPoint && connectedCurve && !includeLastPoint) {
						continue;
					}
				}

				float stepC = (float)j / (float)sampleRate;
				float stepF = stepC + 0.005f;
				float stepB = stepC - 0.005f;

				Vector3 posC = GetPoint (p0, p1, stepC);
				Vector3 posF = GetPoint (p0, p1, stepF);
				Vector3 posB = GetPoint (p0, p1, stepB);

				Vector3 tangent = (posF - posB);
				Vector3 normal = Vector3.Cross (tangent, Vector3.forward);

				curvePoints.Add (new CurvePoint (posC, normal, tangent, i1));
			}
		}

		return curvePoints;
	}

	public List<CurvePoint> GetPointsWithTangentAdjustment (bool includeLastPoint = true, bool createDoublePointsOnSharpEdges = false)
	{
		List<CurvePoint> curvePoints = new List<CurvePoint> ();

		if (points.Count == 1) {
			curvePoints.Add (new CurvePoint (points [0].GetPosition (), Vector3.zero, Vector3.zero));
			return curvePoints;
		}

		int loopCount = points.Count - 1;
		if (connectedCurve && points.Count > 1)
			loopCount++;

		for (int i = 0; i < loopCount; i++) {
			int i0 = i;
			int i1 = (i + 1) % points.Count;
			int lc = loopCount - 1;
			bool isLastAnchorPoint = i == lc;

			BezierPoint p0 = points [i0];
			BezierPoint p1 = points [i1];

			float accumulatedDistance = 0.0f;
			float tangentDot = 1.0f;
			Vector3 lastTangent = Vector3.zero;
			Vector3 lastPosition = Vector3.zero;

			for (int j = 0; j <= sampleRate; j++) {
				bool isLastPointOnCurvePart = j == sampleRate;
				bool c1 = isLastPointOnCurvePart && !isLastAnchorPoint;
				bool isLastPoint = isLastPointOnCurvePart && isLastAnchorPoint;
				bool c2 = c1 && p1.pointType != BezierPointType.Connected && createDoublePointsOnSharpEdges;

				if (!c2) {
					if (c1 || isLastPoint && connectedCurve && !includeLastPoint) {
						continue;
					}
				}

				float stepC = (float)j / (float)sampleRate;
				float stepF = stepC + 0.005f;
				float stepB = stepC - 0.005f;

				Vector3 posC = GetPoint (p0, p1, stepC);
				Vector3 posF = GetPoint (p0, p1, stepF);
				Vector3 posB = GetPoint (p0, p1, stepB);

				Vector3 tangent = (posF - posB);
				Vector3 normal = Vector3.Cross (tangent, Vector3.forward);

				CurvePoint curvePoint = new CurvePoint (posC, normal, tangent, i1);

				if (isLastPointOnCurvePart) {
					curvePoints.Add (curvePoint);
					continue;
				}

				if (j > 0) {
					tangentDot = Vector3.Dot (tangent.normalized, lastTangent.normalized);
					accumulatedDistance += Vector3.Distance (posC, lastPosition);

					if (tangentDot < 1.0f - tangentLimit || accumulatedDistance >= minDistance) {
						lastPosition = posC;
						lastTangent = tangent;
						curvePoints.Add (curvePoint);
						accumulatedDistance = 0.0f;
					}
				} else {
					lastPosition = posC;
					lastTangent = tangent;
					curvePoints.Add (curvePoint);
				}
			}
		}

		return curvePoints;
	}

	public float GetLength ()
	{
		int loopCount = points.Count - 1;
		float length = 0;

		if (connectedCurve && points.Count > 1)
			loopCount++;

		for (int i = 0; i < loopCount; i++) {
			int i0 = i;
			int i1 = (i + 1) % points.Count;

			length += ApproximateLength (points [i0], points [i1], sampleRate);
		}

		return length;
	}

	public float ApproximateLength (BezierPoint p0, BezierPoint p1, int sampleRate)
	{
		float length = 0;
		Vector3 lastPosition = p0.GetPosition ();
		Vector3 currentPosition;

		for (int i = 0; i <= sampleRate; i++) {
			currentPosition = GetPoint (p0, p1, i / sampleRate);
			length += (currentPosition - lastPosition).magnitude;
			lastPosition = currentPosition;
		}

		return length;
	}

	public Vector3 BezierCurveLinear (Vector3 p0, Vector3 p1, float t)
	{
		return (1.0f - t) * p0 + t * p1;
	}

	public Vector3 QuadraticCurve (Vector3 p0, Vector3 p1, Vector3 p2, float t)
	{
		float nt = 1.0f - t;
		return Mathf.Pow (nt, 2) * p0 + 2.0f * nt * t * p1 + Mathf.Pow (t, 2) * p2;
	}

	public Vector3 CubicCurve (Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
	{
		float nt = 1.0f - t;
		return Mathf.Pow (nt, 3) * p0 + 3.0f * Mathf.Pow (nt, 2) * t * p1 + 3.0f * nt * Mathf.Pow (t, 2) * p2 + Mathf.Pow (t, 3) * p3;
	}

	public BezierCurveData Save (string assetPath)
	{
		BezierCurveData bezierCurveData = ScriptableObject.CreateInstance<BezierCurveData> ();

		bezierCurveData.points = new List<BezierPoint> (points);
		bezierCurveData.connectedCurve = connectedCurve;
		bezierCurveData.handleSize = handleSize;
		bezierCurveData.snapSize = snapSize;
		bezierCurveData.sampleRate = sampleRate;

		AssetDatabase.CreateAsset (bezierCurveData, assetPath);
		AssetDatabase.SaveAssets ();
		AssetDatabase.Refresh ();

		return AssetDatabase.LoadAssetAtPath<BezierCurveData> (assetPath);
	}

	public void Load (BezierCurveData bezierCurveData)
	{
		List<BezierPoint> bezierPoints = bezierCurveData.points;
		for (int i = 0; i < bezierPoints.Count; i++) {
			AddAnchorPoint (bezierPoints [i]);
		}

		connectedCurve = bezierCurveData.connectedCurve;
		handleSize = bezierCurveData.handleSize;
		snapSize = bezierCurveData.snapSize;
		sampleRate = bezierCurveData.sampleRate;
	}
}
