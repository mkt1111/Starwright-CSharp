namespace Starwright.Core;

/// <summary>Double-precision quaternion (x, y, z, w), matching three.js multiply order.</summary>
public sealed class DQuat
{
    public double x, y, z, w;

    public DQuat(double x = 0, double y = 0, double z = 0, double w = 1)
    {
        this.x = x; this.y = y; this.z = z; this.w = w;
    }

    public DQuat set(double x, double y, double z, double w)
    {
        this.x = x; this.y = y; this.z = z; this.w = w;
        return this;
    }

    public DQuat identity() { x = 0; y = 0; z = 0; w = 1; return this; }
    public DQuat copy(DQuat q) { x = q.x; y = q.y; z = q.z; w = q.w; return this; }
    public DQuat clone() => new(x, y, z, w);

    public double lengthSq() => x * x + y * y + z * z + w * w;
    public double length() => Math.Sqrt(lengthSq());

    public DQuat normalize()
    {
        var l = length();
        if (l > 0) { var k = 1 / l; x *= k; y *= k; z *= k; w *= k; }
        return this;
    }

    public DQuat conjugate() { x = -x; y = -y; z = -z; return this; }

    public DQuat invert()
    {
        var l2 = lengthSq();
        if (l2 > 0) { var k = 1 / l2; x = -x * k; y = -y * k; z = -z * k; w *= k; }
        return this;
    }

    public double dot(DQuat q) => x * q.x + y * q.y + z * q.z + w * q.w;

    public DQuat multiplyQuaternions(DQuat a, DQuat b)
    {
        var ax = a.x; var ay = a.y; var az = a.z; var aw = a.w;
        var bx = b.x; var by = b.y; var bz = b.z; var bw = b.w;
        x = ax * bw + aw * bx + ay * bz - az * by;
        y = ay * bw + aw * by + az * bx - ax * bz;
        z = az * bw + aw * bz + ax * by - ay * bx;
        w = aw * bw - ax * bx - ay * by - az * bz;
        return this;
    }

    public DQuat multiply(DQuat q) => multiplyQuaternions(this, q);
    public DQuat premultiply(DQuat q) => multiplyQuaternions(q, this);

    public DQuat setFromAxisAngle(DVec3 axis, double angle)
    {
        var half = angle * 0.5;
        var s = Math.Sin(half);
        x = axis.x * s; y = axis.y * s; z = axis.z * s; w = Math.Cos(half);
        return this;
    }

    /// <summary>Columns of the rotation are the three basis vectors (three.js Matrix4.makeBasis).</summary>
    public DQuat setFromBasis(DVec3 xAxis, DVec3 yAxis, DVec3 zAxis)
    {
        var m11 = xAxis.x; var m12 = yAxis.x; var m13 = zAxis.x;
        var m21 = xAxis.y; var m22 = yAxis.y; var m23 = zAxis.y;
        var m31 = xAxis.z; var m32 = yAxis.z; var m33 = zAxis.z;
        var trace = m11 + m22 + m33;
        if (trace > 0)
        {
            var s = 0.5 / Math.Sqrt(trace + 1);
            w = 0.25 / s;
            x = (m32 - m23) * s;
            y = (m13 - m31) * s;
            z = (m21 - m12) * s;
        }
        else if (m11 > m22 && m11 > m33)
        {
            var s = 2 * Math.Sqrt(1 + m11 - m22 - m33);
            w = (m32 - m23) / s;
            x = 0.25 * s;
            y = (m12 + m21) / s;
            z = (m13 + m31) / s;
        }
        else if (m22 > m33)
        {
            var s = 2 * Math.Sqrt(1 + m22 - m11 - m33);
            w = (m13 - m31) / s;
            x = (m12 + m21) / s;
            y = 0.25 * s;
            z = (m23 + m32) / s;
        }
        else
        {
            var s = 2 * Math.Sqrt(1 + m33 - m11 - m22);
            w = (m21 - m12) / s;
            x = (m13 + m31) / s;
            y = (m23 + m32) / s;
            z = 0.25 * s;
        }
        return this;
    }

    public DQuat setFromUnitVectors(DVec3 vFrom, DVec3 vTo)
    {
        const double eps = 1e-6;
        var r = vFrom.dot(vTo) + 1;
        if (r < eps)
        {
            r = 0;
            if (Math.Abs(vFrom.x) > Math.Abs(vFrom.z)) { x = -vFrom.y; y = vFrom.x; z = 0; w = r; }
            else { x = 0; y = -vFrom.z; z = vFrom.y; w = r; }
        }
        else
        {
            x = vFrom.y * vTo.z - vFrom.z * vTo.y;
            y = vFrom.z * vTo.x - vFrom.x * vTo.z;
            z = vFrom.x * vTo.y - vFrom.y * vTo.x;
            w = r;
        }
        return normalize();
    }

    public double angleTo(DQuat q)
    {
        var d = Math.Abs(dot(q));
        if (d > 1) d = 1;
        return 2 * Math.Acos(d);
    }

    public DQuat slerp(DQuat qb, double t)
    {
        if (t <= 0) return this;
        if (t >= 1) return copy(qb);
        var ax = x; var ay = y; var az = z; var aw = w;
        var bx = qb.x; var by = qb.y; var bz = qb.z; var bw = qb.w;
        var cos = ax * bx + ay * by + az * bz + aw * bw;
        if (cos < 0) { cos = -cos; bx = -bx; by = -by; bz = -bz; bw = -bw; }
        double s0, s1;
        if (cos > 0.9995)
        {
            s0 = 1 - t; s1 = t;
        }
        else
        {
            var omega = Math.Acos(cos);
            var inv = 1 / Math.Sin(omega);
            s0 = Math.Sin((1 - t) * omega) * inv;
            s1 = Math.Sin(t * omega) * inv;
        }
        x = ax * s0 + bx * s1; y = ay * s0 + by * s1; z = az * s0 + bz * s1; w = aw * s0 + bw * s1;
        return normalize();
    }

    public DQuat rotateTowards(DQuat q, double step)
    {
        var angle = angleTo(q);
        if (angle == 0) return this;
        var t = Math.Min(1, step / angle);
        return slerp(q, t);
    }
}
