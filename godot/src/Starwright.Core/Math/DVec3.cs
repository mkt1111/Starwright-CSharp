namespace Starwright.Core;

/// <summary>
/// Double-precision vector. Methods mutate this and return it, matching three.js Vector3,
/// so the orbital sim keeps the same algebra as the JavaScript game. Godot's Vector3 is
/// single precision and is used only at the render edge.
/// </summary>
public sealed class DVec3
{
    public double x, y, z;

    public DVec3(double x = 0, double y = 0, double z = 0)
    {
        this.x = x; this.y = y; this.z = z;
    }

    public DVec3 set(double x, double y, double z) { this.x = x; this.y = y; this.z = z; return this; }
    public DVec3 copy(DVec3 v) { x = v.x; y = v.y; z = v.z; return this; }
    public DVec3 clone() => new(x, y, z);

    public DVec3 add(DVec3 v) { x += v.x; y += v.y; z += v.z; return this; }
    public DVec3 sub(DVec3 v) { x -= v.x; y -= v.y; z -= v.z; return this; }
    public DVec3 multiplyScalar(double s) { x *= s; y *= s; z *= s; return this; }
    public DVec3 addScaledVector(DVec3 v, double s) { x += v.x * s; y += v.y * s; z += v.z * s; return this; }
    public DVec3 negate() { x = -x; y = -y; z = -z; return this; }

    public double dot(DVec3 v) => x * v.x + y * v.y + z * v.z;
    public double lengthSq() => x * x + y * y + z * z;
    public double length() => Math.Sqrt(lengthSq());

    public DVec3 normalize()
    {
        var l = length();
        if (l > 0) { var k = 1 / l; x *= k; y *= k; z *= k; }
        return this;
    }

    public DVec3 setLength(double len)
    {
        var l = length();
        if (l > 0) multiplyScalar(len / l);
        return this;
    }

    public DVec3 crossVectors(DVec3 a, DVec3 b)
    {
        var ax = a.x; var ay = a.y; var az = a.z;
        var bx = b.x; var by = b.y; var bz = b.z;
        x = ay * bz - az * by;
        y = az * bx - ax * bz;
        z = ax * by - ay * bx;
        return this;
    }

    public DVec3 cross(DVec3 v) => crossVectors(this, v);

    public double distanceTo(DVec3 v)
    {
        var dx = x - v.x; var dy = y - v.y; var dz = z - v.z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public double distanceToSquared(DVec3 v)
    {
        var dx = x - v.x; var dy = y - v.y; var dz = z - v.z;
        return dx * dx + dy * dy + dz * dz;
    }

    public DVec3 lerp(DVec3 v, double t)
    {
        x += (v.x - x) * t; y += (v.y - y) * t; z += (v.z - z) * t;
        return this;
    }

    public DVec3 applyQuaternion(DQuat q)
    {
        var qx = q.x; var qy = q.y; var qz = q.z; var qw = q.w;
        var ix = qw * x + qy * z - qz * y;
        var iy = qw * y + qz * x - qx * z;
        var iz = qw * z + qx * y - qy * x;
        var iw = -qx * x - qy * y - qz * z;
        x = ix * qw + iw * -qx + iy * -qz - iz * -qy;
        y = iy * qw + iw * -qy + iz * -qx - ix * -qz;
        z = iz * qw + iw * -qz + ix * -qy - iy * -qx;
        return this;
    }

    public DVec3 applyAxisAngle(DVec3 axis, double angle)
    {
        var q = new DQuat();
        q.setFromAxisAngle(axis, angle);
        return applyQuaternion(q);
    }

    public double angleTo(DVec3 v)
    {
        var den = Math.Sqrt(lengthSq() * v.lengthSq());
        if (den == 0) return Math.PI / 2;
        var theta = dot(v) / den;
        if (theta > 1) theta = 1; else if (theta < -1) theta = -1;
        return Math.Acos(theta);
    }

    public double[] toArray() => new[] { x, y, z };

    public DVec3 fromArray(double[] a)
    {
        x = a[0]; y = a[1]; z = a[2];
        return this;
    }

    public override string ToString() => $"({x}, {y}, {z})";
}
