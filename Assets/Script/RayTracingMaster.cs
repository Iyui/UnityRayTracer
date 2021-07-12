//思路，创建一张RT，将需要绘制的内容先绘制到RT上，最后传入帧缓存显示
using UnityEngine;
public class RayTracingMaster : MonoBehaviour
{
    //定义使用的CS
    public ComputeShader RayTracingShader;
    //绘制的RT
    private RenderTexture _target;
    //用来获取相机矩阵等信息
    private Camera _camera;
    //用来获取天空纹理
    public Texture SkyboxTexture;
    //传入半透明混合的权重系数
    private uint _currentSample = 0;
    //半透明混合的材质
    private Material _addMaterial;
    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void SetShaderParameters()
    {
        //向CS中传递V矩阵，进行相机空间和世界空间转换操作
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        //向CS中传递P转置矩阵，进行相机空间和剪裁空间转换操作
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
        //向CS中传递天空纹理
        RayTracingShader.SetTexture(0, "_SkyboxTexture", SkyboxTexture);
        RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
    }

    //完成所有渲染之后调用，通常用于后处理
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        //之前设置参数忘了调用了，感谢 @林一 的提醒。
        SetShaderParameters();
        Render(destination);
    }

    private void Render(RenderTexture destination)
    {
        // 创建RT
        InitRenderTexture();
        // 设置渲染目标并执行ComputeShader
        RayTracingShader.SetTexture(0, "Result", _target);
        //根据图素和CS中定义的线程数(8,8,1)，计算x，y维度上的线程工作组数量
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        //在0号内核上执行，x维度工作组数，y维度工作组数，z维度工作组数
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        // 将RT绘制入帧缓存显示
        Graphics.Blit(_target, destination);
        //这部分创建材质可以移除到Awake()中进行
        if (_addMaterial == null)
            _addMaterial = new Material(Shader.Find("Hidden/AddShader"));
        //传入半透明系数并用该材质绘制帧缓存
        _addMaterial.SetFloat("_Sample", _currentSample);
        Graphics.Blit(_target, destination, _addMaterial);
        //增加半透明系数
        _currentSample++;
    }

    private void InitRenderTexture()
    {
        if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
        {
            // 保证只存在一张RT
            if (_target != null)
                _target.Release();
            // 创建RT
            _target = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();
        }
    }

    //每次相机移动时，重置_currentSample，即重新进行抗锯齿
    private void Update()
    {
        if (transform.hasChanged)
        {
            _currentSample = 0;
            transform.hasChanged = false;
        }
    }
}