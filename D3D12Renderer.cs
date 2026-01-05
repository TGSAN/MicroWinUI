using MicroWinUI;
using SharpDX;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using System;
using System.Text;
using System.Threading;
using Windows.UI.Xaml.Controls;
using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;

namespace HelloXbox
{
    public class D3D12Renderer : IDisposable
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct SceneConstantBuffer
        {
            public Vector4 Right;
            public Vector4 Forward;
            public Vector4 Up;
            public Vector4 Origin;
            public float Len;
            public float X;
            public float Y;
            public float Padding;
        }

        private struct Vertex
        {
            public Vector3 Position;
            public Vector2 TexCoord;
        }

        private const int FrameCount = 2;

        private SwapChainPanel _swapChainPanel;
        private Device _device;
        private CommandQueue _commandQueue;
        private SwapChain3 _swapChain;
        private DescriptorHeap _rtvHeap;
        private int _rtvDescriptorSize;
        private Resource[] _renderTargets;
        private CommandAllocator _commandAllocator;
        private GraphicsCommandList _commandList;
        private PipelineState _pipelineState;
        private RootSignature _rootSignature;
        private Fence _fence;
        private long _fenceValue;
        private AutoResetEvent _fenceEvent;
        private int _frameIndex;
        private Rectangle _scissorRect;

        // Viewport parameters
        private float _vpX, _vpY, _vpWidth, _vpHeight;

        private Resource _vertexBuffer;
        private VertexBufferView _vertexBufferView;
        
        // Constant Buffer
        private Resource _constantBuffer;
        private IntPtr _constantBufferPtr;
        
        // Camera State
        // Camera State
        public float Len { get; set; } = 1.6f;
        public float Ang1 { get; set; } = 2.8f;
        public float Ang2 { get; set; } = 0.4f;
        public float CenX { get; set; } = 0.0f;
        public float CenY { get; set; } = 0.0f;
        public float CenZ { get; set; } = 0.0f;

        private int _adapterIndex = 0;

        public static System.Collections.Generic.List<string> GetHardwareAdapters()
        {
            var adapters = new System.Collections.Generic.List<string>();
            using (var factory = new Factory4())
            {
                foreach (var adapter in factory.Adapters)
                {
                    adapters.Add(adapter.Description.Description);
                    // Do not dispose adapters here as Factory owns them or they are COM content? 
                    // Factory.Adapters property creates new array of adapters, checking SharpDX implementation.
                    // Usually we should dispose them if they are ComObjects.
                    adapter.Dispose();
                }
            }
            return adapters;
        }

        public D3D12Renderer(SwapChainPanel panel, int adapterIndex = 0)
        {
            _swapChainPanel = panel;
            _adapterIndex = adapterIndex;
            LoadPipeline();
            LoadAssets();
        }


        private void LoadPipeline()
        {
            // Fixed render resolution as requested
            var width = 1024;
            var height = 1024;
            
            _vpX = 0;
            _vpY = 0;
            _vpWidth = width;
            _vpHeight = height;

            _scissorRect = new Rectangle(0, 0, width, height);

 #if DEBUG
            //try
            //{
            //    using (var debug = SharpDX.Direct3D12.DebugInterface.Get())
            //    {
            //        if (debug != null)
            //        {
            //            debug.EnableDebugLayer();
            //        }
            //    }
            //}
            //catch (Exception) { /* If debug layer is missing, ignore */ }
#endif

            using (var factory = new Factory4())
            {
                // Select specific adapter
                var adapter = factory.Adapters[_adapterIndex];
                _device = new Device(adapter, SharpDX.Direct3D.FeatureLevel.Level_11_0);
                // Note: Do not dispose adapter from factory.Adapters immediately if using it? 
                // Actually GetAdapter() returns a new com object, Adapters[] might too.
                // Factory.Adapters returns an array of Adapter references.
                // Let's rely on standard dispose flow implicitly or explicitly.
                // adapter.Dispose() is fine after device creation usually.

                var queueDesc = new CommandQueueDescription(CommandListType.Direct);
                _commandQueue = _device.CreateCommandQueue(queueDesc);

                var swapChainDesc1 = new SwapChainDescription1()
                {
                     Width = width,
                     Height = height,
                     Format = Format.R8G8B8A8_UNorm,
                     Stereo = false,
                     SampleDescription = new SampleDescription(1, 0),
                     Usage = Usage.RenderTargetOutput,
                     BufferCount = FrameCount,
                     Scaling = Scaling.Stretch,
                     SwapEffect = SwapEffect.FlipDiscard,
                     AlphaMode = AlphaMode.Ignore,
                     Flags = SwapChainFlags.None
                };

                using (var nativePanel = new SharpDX.ComObject(_swapChainPanel))
                {
                    var iscpn = nativePanel.QueryInterface<SharpDX.DXGI.ISwapChainPanelNative>();
                    using (var swapChain1 = new SwapChain1(factory, _commandQueue, ref swapChainDesc1))
                    {
                        iscpn.SwapChain = swapChain1;
                        _swapChain = swapChain1.QueryInterface<SwapChain3>();
                    }
                    iscpn.Dispose();
                }
            }

            _rtvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription()
            {
                DescriptorCount = FrameCount,
                Type = DescriptorHeapType.RenderTargetView,
                Flags = DescriptorHeapFlags.None
            });

            _rtvDescriptorSize = _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);

            _renderTargets = new Resource[FrameCount];
            var rtvHandle = _rtvHeap.CPUDescriptorHandleForHeapStart;
            for (var i = 0; i < FrameCount; i++)
            {
                _renderTargets[i] = _swapChain.GetBackBuffer<Resource>(i);
                _device.CreateRenderTargetView(_renderTargets[i], null, rtvHandle);
                rtvHandle += _rtvDescriptorSize;
            }

            _commandAllocator = _device.CreateCommandAllocator(CommandListType.Direct);
        }

        private void LoadAssets()
        {
            // Root Signature with 1 Constant Buffer View (Root Descriptor)
            var rootParameter = new RootParameter(ShaderVisibility.All, new RootDescriptor(0, 0), RootParameterType.ConstantBufferView);
            var rootSignatureDesc = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, new [] { rootParameter });
            _rootSignature = _device.CreateRootSignature(rootSignatureDesc.Serialize());

            // Load shaders
            var shaderBytes = Resources.Shaders;
            var shaderSource = Encoding.UTF8.GetString(shaderBytes);
            
            var vertexShaderResult = SharpDX.D3DCompiler.ShaderBytecode.Compile(shaderSource, "VSMain", "vs_5_0");
            var pixelShaderResult = SharpDX.D3DCompiler.ShaderBytecode.Compile(shaderSource, "PSMain", "ps_5_0");
            var vertexShader = new SharpDX.Direct3D12.ShaderBytecode(vertexShaderResult.Bytecode);
            var pixelShader = new SharpDX.Direct3D12.ShaderBytecode(pixelShaderResult.Bytecode);

            var inputElementDescs = new []
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
            };

            var psoDesc = new GraphicsPipelineStateDescription()
            {
                InputLayout = new InputLayoutDescription(inputElementDescs),
                RootSignature = _rootSignature,
                VertexShader = vertexShader,
                PixelShader = pixelShader,
                RasterizerState = new RasterizerStateDescription() { CullMode = CullMode.None, FillMode = FillMode.Solid, IsDepthClipEnabled = false },
                BlendState = BlendStateDescription.Default(),
                DepthStencilFormat = Format.Unknown,
                DepthStencilState = new DepthStencilStateDescription() { IsDepthEnabled = false, IsStencilEnabled = false },
                SampleMask = int.MaxValue,
                PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
                RenderTargetCount = 1,
                Flags = PipelineStateFlags.None,
                SampleDescription = new SampleDescription(1, 0),
                StreamOutput = new StreamOutputDescription()
            };
            psoDesc.RenderTargetFormats[0] = Format.R8G8B8A8_UNorm;

            _pipelineState = _device.CreateGraphicsPipelineState(psoDesc);

            _commandList = _device.CreateCommandList(CommandListType.Direct, _commandAllocator, _pipelineState);
            _commandList.Close();

            // Vertex Buffer - Full Screen Quad
            // We don't strictly need vertices for a full screen quad if we use SV_VertexID in shader, 
            // but the original code uses an attribute 'position'.
            // The shader expects position to be passed.
            var vertices = new Vertex[]
            {
                new Vertex() { Position = new Vector3(-1.0f, -1.0f, 0.0f), TexCoord = new Vector2(0.0f, 1.0f) },
                new Vertex() { Position = new Vector3(1.0f, -1.0f, 0.0f), TexCoord = new Vector2(1.0f, 1.0f) },
                new Vertex() { Position = new Vector3(1.0f,  1.0f, 0.0f), TexCoord = new Vector2(1.0f, 0.0f) },
                new Vertex() { Position = new Vector3(-1.0f, -1.0f, 0.0f), TexCoord = new Vector2(0.0f, 1.0f) },
                new Vertex() { Position = new Vector3(1.0f,  1.0f, 0.0f), TexCoord = new Vector2(1.0f, 0.0f) },
                new Vertex() { Position = new Vector3(-1.0f,  1.0f, 0.0f), TexCoord = new Vector2(0.0f, 0.0f) }
            };
            
            int vertexBufferSize = Utilities.SizeOf(vertices);
            _vertexBuffer = _device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(vertexBufferSize),
                ResourceStates.GenericRead,
                null);

            var ptr = _vertexBuffer.Map(0);
            Utilities.Write(ptr, vertices, 0, vertices.Length);
            _vertexBuffer.Unmap(0);

            _vertexBufferView = new VertexBufferView()
            {
                BufferLocation = _vertexBuffer.GPUVirtualAddress,
                StrideInBytes = Utilities.SizeOf<Vector3>() + Utilities.SizeOf<Vector2>(),
                SizeInBytes = vertexBufferSize
            };

            _fence = _device.CreateFence(0, FenceFlags.None);
            _fenceValue = 1;
            _fenceEvent = new AutoResetEvent(false);

            // Create Constant Buffer
            int constantBufferSize = Utilities.SizeOf<SceneConstantBuffer>();
            constantBufferSize = (constantBufferSize + 255) & ~255; // Align to 256 bytes

            _constantBuffer = _device.CreateCommittedResource(
                new HeapProperties(HeapType.Upload),
                HeapFlags.None,
                ResourceDescription.Buffer(constantBufferSize),
                ResourceStates.GenericRead,
                null);

            _constantBufferPtr = _constantBuffer.Map(0);

            WaitForPreviousFrame();
        }

        public void Render()
        {
            // Use fixed viewport size 1024x1024 for 1:1 aspect ratio calculation
            // This coupled with Scaling.AspectRatioStretch ensures correct circles without distortion
            float cx = _vpWidth;
            float cy = _vpHeight;
            // Avoid divide by zero
            if (cx + cy == 0) { cx = 1; cy = 1; }
            
            float aspectX = cx * 2.0f / (cx + cy);
            float aspectY = cy * 2.0f / (cx + cy);
            
            float cosAng1 = (float)Math.Cos(Ang1);
            float sinAng1 = (float)Math.Sin(Ang1);
            float cosAng2 = (float)Math.Cos(Ang2);
            float sinAng2 = (float)Math.Sin(Ang2);
            
            Vector3 origin = new Vector3(
                Len * cosAng1 * cosAng2 + CenX,
                Len * sinAng2 + CenY,
                Len * sinAng1 * cosAng2 + CenZ
            );
            
            Vector3 right = new Vector3(sinAng1, 0, -cosAng1);
            Vector3 up = new Vector3(-sinAng2 * cosAng1, cosAng2, -sinAng2 * sinAng1);
            Vector3 forward = new Vector3(-cosAng1 * cosAng2, -sinAng2, -sinAng1 * cosAng2);
            
            var cbData = new SceneConstantBuffer
            {
                Right = new Vector4(right, 0),
                Forward = new Vector4(forward, 0),
                Up = new Vector4(up, 0),
                Origin = new Vector4(origin, 0),
                Len = Len,
                X = aspectX,
                Y = aspectY,
                Padding = 0
            };
            
            Utilities.Write(_constantBufferPtr, ref cbData);

            _commandAllocator.Reset();
            _commandList.Reset(_commandAllocator, _pipelineState);

            _commandList.SetGraphicsRootSignature(_rootSignature);
            
            // Set Constant Buffer View (Root Descriptor)
            _commandList.SetGraphicsRootConstantBufferView(0, _constantBuffer.GPUVirtualAddress);
            
            var viewport = new RawViewportF 
            { 
                 X = _vpX, Y = _vpY, Width = _vpWidth, Height = _vpHeight, 
                 MinDepth = 0.0f, MaxDepth = 1.0f 
            };
            _commandList.SetViewport(viewport);

            _commandList.SetScissorRectangles(_scissorRect);

            var rtvHandle = _rtvHeap.CPUDescriptorHandleForHeapStart;
            rtvHandle += _frameIndex * _rtvDescriptorSize;
            
            _commandList.ResourceBarrierTransition(_renderTargets[_frameIndex], ResourceStates.Present, ResourceStates.RenderTarget);
            
            _commandList.SetRenderTargets(rtvHandle, null);
            _commandList.ClearRenderTargetView(rtvHandle, new Color4(0.0f, 0.2f, 0.4f, 1.0f), 0, null);

            _commandList.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            _commandList.SetVertexBuffer(0, _vertexBufferView);
            // Draw 2 triangles (6 vertices) for full screen quad
            _commandList.DrawInstanced(6, 1, 0, 0);

            _commandList.ResourceBarrierTransition(_renderTargets[_frameIndex], ResourceStates.RenderTarget, ResourceStates.Present);

            _commandList.Close();

            _commandQueue.ExecuteCommandList(_commandList);

            _swapChain.Present(1, PresentFlags.None);

            WaitForPreviousFrame();
        }

        private void WaitForPreviousFrame()
        {
            var fence = _fenceValue;
            _commandQueue.Signal(_fence, fence);
            _fenceValue++;

            if (_fence.CompletedValue < fence)
            {
                _fence.SetEventOnCompletion(fence, _fenceEvent.SafeWaitHandle.DangerousGetHandle());
                _fenceEvent.WaitOne();
            }

            _frameIndex = _swapChain.CurrentBackBufferIndex;
        }
        
        public void OnResize()
        {
             // TODO implementation for next phase
        }

        public void Dispose()
        {
            WaitForPreviousFrame();
            _swapChainPanel = null;
            // Dispose all DX objects
             _fence.Dispose();
             _rootSignature.Dispose();
             _pipelineState.Dispose();
             _commandList.Dispose();
             _commandAllocator.Dispose();
             _vertexBuffer.Dispose();
             _constantBuffer.Unmap(0);
             _constantBuffer.Dispose();
             _rtvHeap.Dispose();
             foreach(var rt in _renderTargets) rt.Dispose();
             _swapChain.Dispose();
             _commandQueue.Dispose();
             _device.Dispose();
        }
    }
}
