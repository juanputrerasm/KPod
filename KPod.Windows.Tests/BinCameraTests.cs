using System.Numerics;
using KPod.Windows.UI;

namespace KPod.Windows.Tests;

/// <summary>
/// The view-projection convention. System.Numerics transforms row vectors and OpenGL
/// column vectors, and getting the two confused projects the model through the transpose
/// of the camera, which looks like a kaleidoscope rather than like nothing at all.
/// </summary>
public class BinCameraTests
{
    private static readonly Vector3 Eye = new(0, 0, 10);
    private static readonly Vector3 Target = Vector3.Zero;
    private const float Near = 1f;
    private const float Far = 100f;

    [Fact]
    public void WhatTheCameraLooksAtLandsInTheMiddleOfTheWindow()
    {
        Vector3 ndc = Project(Target);

        Assert.Equal(0, ndc.X, 4);
        Assert.Equal(0, ndc.Y, 4);
    }

    [Fact]
    public void NearPlaneIsMinusOneAndFarPlaneIsPlusOne()
    {
        // Straight down the view axis: the camera sits at z = 10 looking towards zero.
        Assert.Equal(-1, Project(new Vector3(0, 0, 10 - Near)).Z, 3);
        Assert.Equal(1, Project(new Vector3(0, 0, 10 - Far)).Z, 3);
    }

    [Fact]
    public void RightOfTheTargetIsRightOfTheScreenAndAboveIsAbove()
    {
        Assert.True(Project(new Vector3(1, 0, 0)).X > 0);
        Assert.True(Project(new Vector3(0, 1, 0)).Y > 0);
    }

    [Fact]
    public void AWideWindowSpreadsTheSameModelOverLessOfTheWidth()
    {
        float square = Project(new Vector3(1, 0, 0), aspect: 1f).X;
        float wide = Project(new Vector3(1, 0, 0), aspect: 2f).X;

        Assert.True(wide < square);
    }

    [Fact]
    public void PointsBehindTheCameraFallOutsideTheClipVolume()
    {
        Vector4 clip = Vector4.Transform(new Vector4(0, 0, 20, 1),
            BinOpenGlRenderer.ViewProjection(Eye, Target, 1f, Near, Far));

        Assert.True(clip.W <= 0);
    }

    /// <summary>
    /// Transforms the way System.Numerics does, as a row vector, which is the convention
    /// the uploaded matrix has to be in before OpenGL reinterprets it.
    /// </summary>
    private static Vector3 Project(Vector3 point, float aspect = 1f)
    {
        Matrix4x4 viewProjection = BinOpenGlRenderer.ViewProjection(Eye, Target, aspect, Near, Far);
        Vector4 clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
        Assert.True(clip.W > 0, "The point is behind the camera; nothing can be said about its screen position.");
        return new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
    }
}
