//思路，创建一张RT，将需要绘制的内容先绘制到RT上，最后传入帧缓存显示
using UnityEngine;
using System.Collections.Generic;
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

    //球随机种子，用来产生球体生成时的随机数
    public int SphereSeed;
    //球体半径，定义一个最小，一个最大值，进行随机
    public Vector2 SphereRadius = new Vector2(3.0f, 8.0f);
    //场景中的球体数量
    public uint SpheresMax = 100;
    //场景中的球的随机分布半径
    public float SpherePlacementRadius = 100.0f;
    //定义一个传入computeshader的缓存，将生成的所有球体传入CS
    private ComputeBuffer _sphereBuffer;
    public Light DirectionalLight;
    //定义球结构体，用来存储球体的所有信息，暂时只需要位置和半径信息
    struct Sphere
    {
        public Vector3 position;
        public float radius;
    };
    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    //脚本生效时，对场景进行创建，使用SetUpScene()函数
    private void OnEnable()
    {
        _currentSample = 0;
        SetUpScene();
    }//创建场景
    private void SetUpScene()
    {
        //种子，避免每次重新生成场景
        Random.InitState(SphereSeed);
        //定义球列表，用来存放已生成的球
        List<Sphere> spheres = new List<Sphere>();
        // 创建一定数量的球
        for (int i = 0; i < SpheresMax; i++)
        {
            Sphere sphere = new Sphere();
            // 随机球体的半径
            sphere.radius = SphereRadius.x + Random.value * (SphereRadius.y - SphereRadius.x);
            //以环形分布产生二维随机数并乘上分布半径
            Vector2 randomPos = Random.insideUnitCircle * SpherePlacementRadius;
            //将随机数字赋予球体位置的x，z，高度y固定为球体半径(球体均贴地)
            sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);
            // 判断球体之间的相交性
            foreach (Sphere other in spheres)
            {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < minDist * minDist)
                    goto SkipSphere;
            }
            // 将符合的球体填入列表
            spheres.Add(sphere);
        //未通过相交检测的球跳过
        SkipSphere:
            continue;
        }
        // 将球体列表塞入缓存
        //这里注意，申请缓存需要至少如下两个参数1、缓存元素个数  2、每元素字节数
        //我们只需要4个浮点数，4 * 4即16字节大小
        _sphereBuffer = new ComputeBuffer(spheres.Count, 16);
        //塞入数据
        _sphereBuffer.SetData(spheres);
    }
    //脚本销毁时，释放ComputeBuffer 
    private void OnDisable()
    {
        if (_sphereBuffer != null)
            _sphereBuffer.Release();
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
        RayTracingShader.SetBuffer(0, "_Spheres", _sphereBuffer);

        Vector3 l = DirectionalLight.transform.forward;
        RayTracingShader.SetVector("_DirectionalLight", new Vector4(l.x, l.y, l.z, DirectionalLight.intensity));
    }

    //完成所有渲染之后调用，通常用于后处理
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
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