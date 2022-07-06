using UnityEngine;
using System.Collections;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class Temporal : MonoBehaviour
{
    public Vector2 pastoffset;

    //For shader, material and camera, check if null, if so retrieve or create a new one.
    private Shader tempShader;
    public Shader shader
    {
        get
        {
            if (tempShader == null)
                tempShader = Shader.Find("Hidden/TemporalShader");

            return tempShader;
        }
    }

    private Material tempMaterial;
    public Material material
    {
        get
        {
            if (tempMaterial == null)
            {
                if (shader == null || !shader.isSupported)
                    return null;

                tempMaterial = new Material(shader);
            }

            return tempMaterial;
        }
    }

    private Camera tempCamera;
    public new Camera camera
    {
        get
        {
            if (tempCamera == null)
                tempCamera = GetComponent<Camera>();

            return tempCamera;
        }
    }

    private RenderTexture historyTex;
    private int sampleIndex = 0;



    private float GetHaltonValue(int index, int radix)
    {
        float fraction = 1.0f / (float)radix;
        float result = 0.0f;

        while (index > 0)
        {
            result += (float)(index % radix) * fraction;
            index /= radix;
            fraction /= (float)radix;
        }

        return result;
    }

    private Vector2 GenerateRandomOffset()
    {
        Vector2 offset = new Vector2(GetHaltonValue(sampleIndex, 2), GetHaltonValue(sampleIndex, 3));

        if (++sampleIndex >= 16) sampleIndex = 0;

        return offset;
    }

    // Adapted from Playdead's implementation
    // https://github.com/playdeadgames/temporal/blob/master/Assets/Scripts/Extensions.cs
    private Matrix4x4 GetPerspectiveProjectionMatrix(Vector2 offset)
    {
        float v = Mathf.Tan(0.5f * Mathf.Deg2Rad * camera.fieldOfView);
        float h = v * camera.aspect;

        offset.x *= h / (0.5f * camera.pixelWidth);
        offset.y *= v / (0.5f * camera.pixelHeight);

        float left = (offset.x - h) * camera.nearClipPlane;
        float right = (offset.x + h) * camera.nearClipPlane;
        float top = (offset.y + v) * camera.nearClipPlane;
        float bottom = (offset.y - v) * camera.nearClipPlane;

        Matrix4x4 matrix = new Matrix4x4();

        matrix[0, 0] = (2.0f * camera.nearClipPlane) / (right - left);
        matrix[0, 1] = 0.0f;
        matrix[0, 2] = (right + left) / (right - left);
        matrix[0, 3] = 0.0f;

        matrix[1, 0] = 0.0f;
        matrix[1, 1] = (2.0f * camera.nearClipPlane) / (top - bottom);
        matrix[1, 2] = (top + bottom) / (top - bottom);
        matrix[1, 3] = 0.0f;

        matrix[2, 0] = 0.0f;
        matrix[2, 1] = 0.0f;
        matrix[2, 2] = -(camera.farClipPlane + camera.nearClipPlane) / (camera.farClipPlane - camera.nearClipPlane);
        matrix[2, 3] = -(2.0f * camera.farClipPlane * camera.nearClipPlane) / (camera.farClipPlane - camera.nearClipPlane);

        matrix[3, 0] = 0.0f;
        matrix[3, 1] = 0.0f;
        matrix[3, 2] = -1.0f;
        matrix[3, 3] = 0.0f;

        return matrix;
    }

    //Enable depth texture and motion vector rendering on the camera
    void OnEnable()
    {
        camera.depthTextureMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
    }


    //Empty history buffer on disable, turn off motion vectors
    void OnDisable()
    {
        if (historyTex != null)
        {
            RenderTexture.ReleaseTemporary(historyTex);
            historyTex = null;
        }

        camera.depthTextureMode &= ~(DepthTextureMode.MotionVectors);
    }


    //Generate offset from halton, set non jittered matrix to current and set current matrix to the new one with the offset.
    void OnPreCull()
    {
        Vector2 offset = GenerateRandomOffset();
        camera.nonJitteredProjectionMatrix = camera.projectionMatrix;
        camera.projectionMatrix = GetPerspectiveProjectionMatrix(offset);
        pastoffset = offset;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // if no history buffer or history buffer doesn't match render texture size
        if (historyTex == null || (historyTex.width != source.width || historyTex.height != source.height))
        {
            if (historyTex) // previous temporary texture is released if there is one
                RenderTexture.ReleaseTemporary(historyTex);

            // create history texture with the same proportions as source
            historyTex = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);
            historyTex.hideFlags = HideFlags.HideAndDontSave;

            // copy source into history buffer
            Graphics.Blit(source, historyTex);
        }

        // intermediate temporary texture same size as source tex
        RenderTexture temporary = RenderTexture.GetTemporary(source.width, source.height, 0, source.format, RenderTextureReadWrite.Linear);

        // set history texture in material for use in shader
        material.SetTexture("_HistoryTex", historyTex);

        // divide offset by the proportions of rendertexture and add to shader to unjitter
        pastoffset.x /= source.width;
        pastoffset.y /= source.height;
        material.SetVector("_JitterUV", pastoffset);
    
        // copy and apply shader first into history buffer and then to the screen
        Graphics.Blit(source, temporary, material, 0);
        Graphics.Blit(temporary, historyTex);
        Graphics.Blit(temporary, destination);

        // release temporary each time
        RenderTexture.ReleaseTemporary(temporary);
    }

    void OnPostRender()
    {
        //reset camera after jitter
        camera.ResetProjectionMatrix();
    }
}