using UnityEngine;

namespace Indie3DEditor.EditMesh
{
    /// <summary>
    /// Smallest offset d with d·dir = target for every added direction, in the least-squares
    /// sense. Offsetting each plane at a point by its own distance moves the point onto the
    /// intersection of the offset planes, so every face keeps its normal.
    /// </summary>
    internal struct IndieEditMeshOffsetSolver
    {
        /// <summary>
        /// Keeps the system solvable when the directions do not span 3D; the free axes get
        /// almost no offset.
        /// </summary>
        private const double Regularization = 1e-7;

        private double _m00, _m01, _m02, _m11, _m12, _m22;
        private double _b0, _b1, _b2;

        public void Add(Vector3 direction, float target, float weight = 1f)
        {
            double w = (double)weight * weight;
            double x = direction.x, y = direction.y, z = direction.z;

            _m00 += w * x * x;
            _m01 += w * x * y;
            _m02 += w * x * z;
            _m11 += w * y * y;
            _m12 += w * y * z;
            _m22 += w * z * z;

            _b0 += w * x * target;
            _b1 += w * y * target;
            _b2 += w * z * target;
        }

        /// <summary>
        /// Near-opposite directions push the result far away; its length is capped at
        /// <paramref name="maxLength"/>.
        /// </summary>
        public Vector3 Solve(float maxLength)
        {
            double a = _m00 + Regularization, b = _m01, c = _m02;
            double d = _m11 + Regularization, e = _m12;
            double f = _m22 + Regularization;

            double c00 = d * f - e * e;
            double c01 = c * e - b * f;
            double c02 = b * e - c * d;
            double det = a * c00 + b * c01 + c * c02;

            if (System.Math.Abs(det) < 1e-18)
                return Vector3.zero;

            double c11 = a * f - c * c;
            double c12 = b * c - a * e;
            double c22 = a * d - b * b;
            double inv = 1.0 / det;

            var result = new Vector3(
                (float)((c00 * _b0 + c01 * _b1 + c02 * _b2) * inv),
                (float)((c01 * _b0 + c11 * _b1 + c12 * _b2) * inv),
                (float)((c02 * _b0 + c12 * _b1 + c22 * _b2) * inv)
            );

            return Vector3.ClampMagnitude(result, Mathf.Abs(maxLength));
        }
    }
}
