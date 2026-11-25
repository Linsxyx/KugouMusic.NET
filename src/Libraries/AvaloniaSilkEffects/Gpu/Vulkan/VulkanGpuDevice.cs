using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AvaloniaSilkEffects.Gpu.Shaders;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace AvaloniaSilkEffects.Gpu.Vulkan;

/// <summary>How the Vulkan device is created: which GPU, and the extensions presentation needs.</summary>
internal sealed record VulkanDeviceOptions
{
    /// <summary>The physical device to use (VkPhysicalDeviceIDProperties.deviceUUID); null takes the first suitable one.</summary>
    public byte[]? DeviceUuid { get; init; }
    public byte[]? DeviceLuid { get; init; }
    public IReadOnlyList<string> InstanceExtensions { get; init; } = [];
    public IReadOnlyList<string> DeviceExtensions { get; init; } = [];
    /// <summary>A Vulkan loader; null loads the platform's default one.</summary>
    public Vk? Api { get; init; }
}

/// <summary>
/// The Vulkan 1.1 backend. One command buffer per frame (two frames in flight) plus an upload command buffer
/// submitted ahead of it. Render passes start lazily on the bound target and end when it changes; a full clear
/// becomes the next pass's load op. Vulkan's framebuffer rows and clip space already match OpenGL's memory order,
/// so no flip is needed; the presenter imports the image bottom-up.
/// </summary>
internal sealed unsafe class VulkanGpuDevice : GpuDevice
{
    private const int FramesInFlight = 2;
    private const int OneShotStagingThreshold = 256 * 1024;
    internal const Format ColorFormat = Format.R8G8B8A8Unorm;

    private readonly VulkanMemoryAllocator _allocator;
    private readonly FrameContext[] _frames = new FrameContext[FramesInFlight];
    private readonly Dictionary<GpuCompiledProgram, ProgramObjects> _programs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PipelineKey, Pipeline> _pipelines = [];
    private readonly Dictionary<PassKey, RenderPass> _renderPasses = [];
    private readonly Sampler[] _samplers = new Sampler[3];
    private readonly List<(ulong Serial, Action Destroy)> _garbage = [];
    private readonly List<VkSemaphore> _waitSemaphores = [];
    private readonly List<VkSemaphore> _signalSemaphores = [];
    private readonly VulkanTexture _empty;
    private FrameContext? _recording;
    private int _frameIndex;
    private ulong _serial = 1;
    private ulong _completedSerial;
    private VulkanRenderTarget? _passTarget;
    private PipelineKey? _boundPipeline;
    private GpuRect? _boundViewport;
    private GpuRect? _boundScissor;
    private VulkanRenderTarget? _frameOutput;

    private VulkanGpuDevice(Vk api, Instance instance, PhysicalDevice physicalDevice, Device device, uint queueFamily,
        IReadOnlySet<string> deviceExtensions)
    {
        Vk = api;
        Instance = instance;
        PhysicalDevice = physicalDevice;
        Handle = device;
        QueueFamily = queueFamily;
        EnabledDeviceExtensions = deviceExtensions;
        api.GetDeviceQueue(device, queueFamily, 0, out var queue);
        Queue = queue;
        api.GetPhysicalDeviceProperties(physicalDevice, out var properties);
        Properties = properties;
        Renderer = SilkMarshal.PtrToString((nint)properties.DeviceName) ?? "Vulkan";
        ApiVersion = $"Vulkan {properties.ApiVersion >> 22}.{(properties.ApiVersion >> 12) & 0x3FF}";
        api.GetPhysicalDeviceMemoryProperties(physicalDevice, out var memory);
        MemoryProperties = memory;
        var counts = properties.Limits.FramebufferColorSampleCounts & properties.Limits.FramebufferDepthSampleCounts &
            properties.Limits.FramebufferStencilSampleCounts;
        MaxSamples = counts.HasFlag(SampleCountFlags.Count8Bit) ? 8 : counts.HasFlag(SampleCountFlags.Count4Bit) ? 4 : 1;
        DepthStencilFormat = SupportsAttachment(Format.D24UnormS8Uint, FormatFeatureFlags.DepthStencilAttachmentBit)
            ? Format.D24UnormS8Uint : Format.D32SfloatS8Uint;
        _allocator = new VulkanMemoryAllocator(this);
        for (var index = 0; index < FramesInFlight; index++) _frames[index] = new FrameContext(this);
        _samplers[(int)GpuFilter.Nearest] = CreateSampler(Filter.Nearest, false);
        _samplers[(int)GpuFilter.Linear] = CreateSampler(Filter.Linear, false);
        _samplers[(int)GpuFilter.LinearMipmap] = CreateSampler(Filter.Linear, true);
        // Bound where a shader's sampler has no texture, like GL's texture object 0.
        _empty = (VulkanTexture)CreateTexture(1, 1, [0, 0, 0, 0], GpuFilter.Nearest);
    }

    public Vk Vk { get; }
    public Instance Instance { get; }
    public PhysicalDevice PhysicalDevice { get; }
    public Device Handle { get; }
    public Queue Queue { get; }
    public uint QueueFamily { get; }
    public IReadOnlySet<string> EnabledDeviceExtensions { get; }
    public PhysicalDeviceProperties Properties { get; }
    public PhysicalDeviceMemoryProperties MemoryProperties { get; }
    public Format DepthStencilFormat { get; }
    internal VulkanMemoryAllocator MemoryAllocator => _allocator;
    internal int PendingGarbageCount => _garbage.Count;
    internal long ArenaBytes => _frames.Sum(frame => frame.ArenaBytes);
    public override EffectGpuBackend Backend => EffectGpuBackend.Vulkan;
    public override string ApiVersion { get; }
    public override string Renderer { get; }
    public override int MaxSamples { get; }

    /// <summary>Creates an instance and device; throws <see cref="NotSupportedException"/> when no GPU qualifies.</summary>
    public static VulkanGpuDevice Create(VulkanDeviceOptions options)
    {
        var api = options.Api ?? Vk.GetApi();
        var instanceExtensions = new List<string>(options.InstanceExtensions);
        var portability = OperatingSystem.IsMacOS() && InstanceExtensionAvailable(api, "VK_KHR_portability_enumeration");
        if (portability) instanceExtensions.Add("VK_KHR_portability_enumeration");
        var appName = (byte*)SilkMarshal.StringToPtr("AvaloniaSilkEffects");
        var instanceNames = (byte**)SilkMarshal.StringArrayToPtr(instanceExtensions);
        Instance instance;
        try
        {
            var application = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = appName,
                PEngineName = appName,
                ApiVersion = Vk.Version11,
            };
            var instanceInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &application,
                EnabledExtensionCount = (uint)instanceExtensions.Count,
                PpEnabledExtensionNames = instanceNames,
                Flags = portability ? InstanceCreateFlags.EnumeratePortabilityBitKhr : 0,
            };
            Check(api.CreateInstance(in instanceInfo, null, out instance), "vkCreateInstance");
        }
        finally
        {
            SilkMarshal.Free((nint)appName);
            SilkMarshal.Free((nint)instanceNames);
        }
        try
        {
            uint count = 0;
            api.EnumeratePhysicalDevices(instance, ref count, null);
            var physicalDevices = new PhysicalDevice[count];
            fixed (PhysicalDevice* devices = physicalDevices)
                api.EnumeratePhysicalDevices(instance, ref count, devices);
            foreach (var physical in physicalDevices)
            {
                var extensions = new HashSet<string>(options.DeviceExtensions);
                if (DeviceExtensionAvailable(api, physical, "VK_KHR_portability_subset"))
                    extensions.Add("VK_KHR_portability_subset");
                if (extensions.Any(name => !DeviceExtensionAvailable(api, physical, name))) continue;
                if (!Matches(api, physical, options)) continue;
                var family = GraphicsFamily(api, physical);
                if (family is null) continue;
                var priority = 1f;
                var queueInfo = new DeviceQueueCreateInfo
                {
                    SType = StructureType.DeviceQueueCreateInfo,
                    QueueFamilyIndex = family.Value,
                    QueueCount = 1,
                    PQueuePriorities = &priority,
                };
                var features = new PhysicalDeviceFeatures();
                var deviceNames = (byte**)SilkMarshal.StringArrayToPtr(extensions.ToArray());
                Device device;
                try
                {
                    var deviceInfo = new DeviceCreateInfo
                    {
                        SType = StructureType.DeviceCreateInfo,
                        QueueCreateInfoCount = 1,
                        PQueueCreateInfos = &queueInfo,
                        EnabledExtensionCount = (uint)extensions.Count,
                        PpEnabledExtensionNames = deviceNames,
                        PEnabledFeatures = &features,
                    };
                    Check(api.CreateDevice(physical, in deviceInfo, null, out device), "vkCreateDevice");
                }
                finally
                {
                    SilkMarshal.Free((nint)deviceNames);
                }
                return new VulkanGpuDevice(api, instance, physical, device, family.Value, extensions);
            }
            throw new NotSupportedException("No Vulkan device supports the required features.");
        }
        catch
        {
            api.DestroyInstance(instance, null);
            throw;
        }
    }

    // ---- Resources ---------------------------------------------------------------------------------------------------

    public override GpuTexture CreateTexture(int width, int height, ReadOnlySpan<byte> rgba, GpuFilter filter)
    {
        var levels = filter == GpuFilter.LinearMipmap ? (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1 : 1;
        var image = CreateImage(width, height, ColorFormat, levels, SampleCountFlags.Count1Bit,
            ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit,
            out var memory);
        var view = CreateView(image, ColorFormat, ImageAspectFlags.ColorBit, levels);
        var texture = new VulkanTexture(this, image, memory, view, width, height, filter);
        var frame = EnsureRecording();
        var commands = frame.Uploads();
        VkBuffer stagingBuffer;
        int stagingOffset;
        if (!rgba.IsEmpty && rgba.Length >= OneShotStagingThreshold)
        {
            stagingBuffer = CreateHostBuffer((ulong)rgba.Length, BufferUsageFlags.TransferSrcBit, out var stagingAlloc, out var mapped);
            stagingOffset = 0;
            rgba.CopyTo(new Span<byte>(mapped, rgba.Length));
            var tempBuffer = stagingBuffer;
            var tempAlloc = stagingAlloc;
            Defer(() =>
            {
                Vk.DestroyBuffer(Handle, tempBuffer, null);
                _allocator.Free(tempAlloc);
            });
        }
        else
        {
            var staging = frame.Staging(rgba.IsEmpty ? 4 : rgba.Length, out stagingOffset, out stagingBuffer);
            rgba.CopyTo(staging);
        }
        Barrier(commands, image, ImageAspectFlags.ColorBit, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, 0, levels);
        if (!rgba.IsEmpty)
        {
            var copy = new BufferImageCopy
            {
                BufferOffset = (ulong)stagingOffset,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D((uint)width, (uint)height, 1),
            };
            Vk.CmdCopyBufferToImage(commands, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, &copy);
        }
        var w = width;
        var h = height;
        for (var level = 1u; level < levels; level++)
        {
            Barrier(commands, image, ImageAspectFlags.ColorBit, ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal, level - 1, 1);
            var nw = Math.Max(1, w / 2);
            var nh = Math.Max(1, h / 2);
            var blit = new ImageBlit
            {
                SrcSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level - 1, 0, 1),
                DstSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, level, 0, 1),
            };
            blit.SrcOffsets[1] = new Offset3D(w, h, 1);
            blit.DstOffsets[1] = new Offset3D(nw, nh, 1);
            Vk.CmdBlitImage(commands, image, ImageLayout.TransferSrcOptimal, image, ImageLayout.TransferDstOptimal, 1, &blit, Filter.Linear);
            Barrier(commands, image, ImageAspectFlags.ColorBit, ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal, level - 1, 1);
            w = nw;
            h = nh;
        }
        Barrier(commands, image, ImageAspectFlags.ColorBit, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, levels - 1, 1);
        return texture;
    }

    public override GpuBuffer CreateBuffer(ReadOnlySpan<byte> data, bool index)
    {
        var size = Math.Max(4, data.Length);
        var buffer = CreateHostBuffer((ulong)size, index ? BufferUsageFlags.IndexBufferBit : BufferUsageFlags.VertexBufferBit,
            out var memory, out var mapped);
        data.CopyTo(new Span<byte>(mapped, size));
        var result = new VulkanBuffer(this, buffer, memory, data.Length);
        Resources.Buffer(result.Id, data.Length);
        Resources.Uploaded(data.Length);
        return result;
    }

    public override GpuShader CreateShader(string vertexSource, string fragmentSource, string name)
    {
        var program = GpuShaderCompiler.Compile(vertexSource, fragmentSource, name, GpuShaderTarget.Spirv);
        if (!_programs.ContainsKey(program)) _programs[program] = new ProgramObjects(this, program);
        return new VulkanShader(program);
    }

    public override GpuRenderTarget CreateRenderTarget(bool depthStencil) => new VulkanRenderTarget(this, depthStencil);

    /// <summary>Wraps an image another component owns; it is left in <paramref name="restLayout"/> after each pass.</summary>
    public VulkanRenderTarget WrapImage(Image image, Format format, int width, int height, ImageLayout restLayout) =>
        VulkanRenderTarget.External(this, image, format, width, height, restLayout);

    /// <summary>The next submission waits on <paramref name="semaphore"/> before any graphics work.</summary>
    public void WaitBeforeNextSubmit(VkSemaphore semaphore) => _waitSemaphores.Add(semaphore);

    /// <summary>The next submission signals <paramref name="semaphore"/> when it completes.</summary>
    public void SignalAfterNextSubmit(VkSemaphore semaphore) => _signalSemaphores.Add(semaphore);

    // ---- Frames ------------------------------------------------------------------------------------------------------

    public override void BeginFrame(GpuRenderTarget output)
    {
        EnsureRecording();
        _frameOutput = (VulkanRenderTarget)output;
        base.BeginFrame(output);
    }

    public override void EndFrame() => Submit(wait: false);

    /// <summary>Submits the recorded work; with <paramref name="wait"/>, blocks until it completes.</summary>
    public void Submit(bool wait)
    {
        if (_frameOutput is { } output)
        {
            // A clear with nothing drawn after it still has to reach the output.
            if (output.IsAllocated && (output.PendingColor is not null || output.PendingStencil))
            {
                EnsureRecording();
                BeginPass(output);
            }
            _frameOutput = null;
        }
        if (_recording is null && (_waitSemaphores.Count > 0 || _signalSemaphores.Count > 0)) EnsureRecording();
        var frame = _recording;
        if (frame is null) return;
        EndPass();
        _recording = null;
        frame.Submit(_waitSemaphores, _signalSemaphores, _serial);
        _waitSemaphores.Clear();
        _signalSemaphores.Clear();
        _serial++;
        _frameIndex = (_frameIndex + 1) % FramesInFlight;
        if (wait)
        {
            frame.Wait();
            Collect(frame.Serial);
        }
    }

    /// <summary>Reads a single-sample target back as RGBA8 rows, bottom row first (glReadPixels order).</summary>
    public byte[] ReadPixels(GpuRenderTarget target)
    {
        var source = (VulkanRenderTarget)target;
        if (source.Samples != 1) throw new InvalidOperationException("Read back a single-sample target.");
        var frame = EnsureRecording();
        if (source.PendingColor is not null || source.PendingStencil) BeginPass(source);
        EndPass();
        var size = (ulong)source.Width * (ulong)source.Height * 4;
        var buffer = CreateHostBuffer(size, BufferUsageFlags.TransferDstBit, out var memory, out var mapped);
        try
        {
            var commands = frame.Commands;
            Barrier(commands, source.ColorImage, ImageAspectFlags.ColorBit, source.ColorLayout, ImageLayout.TransferSrcOptimal, 0, 1);
            var copy = new BufferImageCopy
            {
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageExtent = new Extent3D((uint)source.Width, (uint)source.Height, 1),
            };
            Vk.CmdCopyImageToBuffer(commands, source.ColorImage, ImageLayout.TransferSrcOptimal, buffer, 1, &copy);
            Barrier(commands, source.ColorImage, ImageAspectFlags.ColorBit, ImageLayout.TransferSrcOptimal, source.RestLayout, 0, 1);
            source.ColorLayout = source.RestLayout;
            Submit(wait: true);
            var pixels = new byte[size];
            new ReadOnlySpan<byte>(mapped, pixels.Length).CopyTo(pixels);
            if (source.Format == Format.B8G8R8A8Unorm)
                for (var index = 0; index < pixels.Length; index += 4)
                    (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
            return pixels;
        }
        finally
        {
            Vk.DestroyBuffer(Handle, buffer, null);
            _allocator.Free(memory);
        }
    }

    /// <summary>Waits for all GPU work, flushes deferred destructions and trims per-frame streaming arenas.</summary>
    public void ReclaimIdleMemory()
    {
        if (IsDisposed) return;
        Submit(wait: true);
        Vk.DeviceWaitIdle(Handle);
        Collect(ulong.MaxValue);
        foreach (var frame in _frames) frame.Trim();
    }

    // ---- Drawing -----------------------------------------------------------------------------------------------------

    public override void Clear(EffectColor? color, bool stencil = false)
    {
        if (!ColorWrite) color = null;
        var target = CurrentTarget;
        if (stencil) target.EnsureDepthStencil();
        if (!target.HasDepthStencil) stencil = false;
        if (color is null && !stencil) return;
        var rect = Clip(Scissor, target);
        var full = rect.X == 0 && rect.Y == 0 && rect.Width == target.Width && rect.Height == target.Height;
        if (full)
        {
            if (ReferenceEquals(_passTarget, target)) EndPass();
            if (color is { } c) target.PendingColor = c;
            if (stencil) target.PendingStencil = true;
            return;
        }
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var commands = EnsurePass(target);
        var attachments = stackalloc ClearAttachment[2];
        var count = 0u;
        if (color is { } value)
            attachments[count++] = new ClearAttachment(ImageAspectFlags.ColorBit, 0,
                new ClearValue(new ClearColorValue(value.R, value.G, value.B, value.A)));
        if (stencil)
            attachments[count++] = new ClearAttachment(ImageAspectFlags.StencilBit, 0,
                new ClearValue(depthStencil: new ClearDepthStencilValue(1, 0)));
        var clearRect = new ClearRect(new Rect2D(new Offset2D(rect.X, rect.Y), new Extent2D((uint)rect.Width, (uint)rect.Height)), 0, 1);
        Vk.CmdClearAttachments(commands, count, attachments, 1, &clearRect);
    }

    public override void Draw(GpuShader shader, GpuTopology topology, int vertexCount)
    {
        var commands = Prepare((VulkanShader)shader, null, topology);
        Vk.CmdDraw(commands, (uint)vertexCount, 1, 0, 0);
    }

    public override void Draw(GpuShader shader, GpuVertexLayout layout, ReadOnlySpan<byte> vertices, int vertexCount)
    {
        if (vertexCount == 0) return;
        var commands = Prepare((VulkanShader)shader, layout, GpuTopology.Triangles);
        var (buffer, offset) = _recording!.Stream(vertices, 16);
        var bufferOffset = (ulong)offset;
        Vk.CmdBindVertexBuffers(commands, 0, 1, &buffer, &bufferOffset);
        Vk.CmdDraw(commands, (uint)vertexCount, 1, 0, 0);
    }

    public override void Draw(GpuShader shader, GpuVertexLayout layout, GpuBuffer vertices, int firstVertex, int vertexCount)
    {
        var commands = Prepare((VulkanShader)shader, layout, GpuTopology.Triangles);
        var buffer = ((VulkanBuffer)vertices).Handle;
        ulong offset = 0;
        Vk.CmdBindVertexBuffers(commands, 0, 1, &buffer, &offset);
        Vk.CmdDraw(commands, (uint)vertexCount, 1, (uint)firstVertex, 0);
    }

    public override void DrawIndexed(GpuShader shader, GpuVertexLayout layout, GpuBuffer vertices, GpuBuffer indices,
        int firstIndex, int indexCount)
    {
        var commands = Prepare((VulkanShader)shader, layout, GpuTopology.Triangles);
        var buffer = ((VulkanBuffer)vertices).Handle;
        ulong offset = 0;
        Vk.CmdBindVertexBuffers(commands, 0, 1, &buffer, &offset);
        Vk.CmdBindIndexBuffer(commands, ((VulkanBuffer)indices).Handle, 0, IndexType.Uint32);
        Vk.CmdDrawIndexed(commands, (uint)indexCount, 1, (uint)firstIndex, 0, 0);
    }

    public override void Resolve(GpuRenderTarget source, GpuRenderTarget destination)
    {
        var from = (VulkanRenderTarget)source;
        var to = (VulkanRenderTarget)destination;
        if (!from.IsAllocated || !to.IsAllocated || to.Samples != 1 || from.Width != to.Width || from.Height != to.Height)
            throw new InvalidOperationException("MSAA resolve requires an equally sized single-sample destination.");
        EnsureRecording();
        if (from.PendingColor is not null) BeginPass(from);
        EndPass();
        var commands = _recording!.Commands;
        Barrier(commands, from.ColorImage, ImageAspectFlags.ColorBit, from.ColorLayout, ImageLayout.TransferSrcOptimal, 0, 1);
        Barrier(commands, to.ColorImage, ImageAspectFlags.ColorBit, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, 0, 1);
        var subresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1);
        var extent = new Extent3D((uint)from.Width, (uint)from.Height, 1);
        if (from.Samples > 1)
        {
            var region = new ImageResolve { SrcSubresource = subresource, DstSubresource = subresource, Extent = extent };
            Vk.CmdResolveImage(commands, from.ColorImage, ImageLayout.TransferSrcOptimal, to.ColorImage, ImageLayout.TransferDstOptimal, 1, &region);
        }
        else
        {
            var region = new ImageCopy { SrcSubresource = subresource, DstSubresource = subresource, Extent = extent };
            Vk.CmdCopyImage(commands, from.ColorImage, ImageLayout.TransferSrcOptimal, to.ColorImage, ImageLayout.TransferDstOptimal, 1, &region);
        }
        Barrier(commands, from.ColorImage, ImageAspectFlags.ColorBit, ImageLayout.TransferSrcOptimal, from.RestLayout, 0, 1);
        Barrier(commands, to.ColorImage, ImageAspectFlags.ColorBit, ImageLayout.TransferDstOptimal, to.RestLayout, 0, 1);
        from.ColorLayout = from.RestLayout;
        to.ColorLayout = to.RestLayout;
        to.PendingColor = null;
        SetTarget(destination);
    }

    private VulkanRenderTarget CurrentTarget =>
        (VulkanRenderTarget)(Target ?? throw new InvalidOperationException("No render target is bound."));

    private CommandBuffer Prepare(VulkanShader shader, GpuVertexLayout? layout, GpuTopology topology)
    {
        var target = CurrentTarget;
        if (Stencil != GpuStencil.Disabled) target.EnsureDepthStencil();
        var commands = EnsurePass(target);
        var objects = _programs[shader.Program];
        var key = new PipelineKey(shader.Program, layout, Blend, ColorWrite, target.HasDepthStencil ? Stencil : GpuStencil.Disabled,
            topology, new PassKey(target.Format, target.Samples, target.HasDepthStencil, false, false, ImageLayout.Undefined,
                ImageLayout.Undefined));
        if (_boundPipeline != key)
        {
            if (!_pipelines.TryGetValue(key, out var pipeline))
            {
                pipeline = CreatePipeline(objects, key);
                _pipelines.Add(key, pipeline);
            }
            Vk.CmdBindPipeline(commands, PipelineBindPoint.Graphics, pipeline);
            if (target.HasDepthStencil) Vk.CmdSetStencilReference(commands, StencilFaceFlags.FrontAndBack, 1);
            _boundPipeline = key;
        }
        if (_boundViewport != Viewport)
        {
            var v = Viewport;
            var viewport = new Viewport(v.X, v.Y, v.Width, v.Height, 0, 1);
            Vk.CmdSetViewport(commands, 0, 1, &viewport);
            _boundViewport = v;
        }
        var scissor = Clip(Scissor, target);
        if (_boundScissor != scissor)
        {
            var rect = new Rect2D(new Offset2D(scissor.X, scissor.Y), new Extent2D((uint)scissor.Width, (uint)scissor.Height));
            Vk.CmdSetScissor(commands, 0, 1, &rect);
            _boundScissor = scissor;
        }

        var frame = _recording!;
        var set = frame.AllocateSet(objects.SetLayout);
        var writes = stackalloc WriteDescriptorSet[2 + shader.Textures.Length];
        var bufferInfos = stackalloc DescriptorBufferInfo[2];
        var imageInfos = stackalloc DescriptorImageInfo[Math.Max(1, shader.Textures.Length)];
        var dynamicOffsets = stackalloc uint[2];
        uint writeCount = 0, offsetCount = 0;
        var alignment = (int)Properties.Limits.MinUniformBufferOffsetAlignment;
        for (var stage = 0; stage < 2; stage++)
        {
            var block = stage == 0 ? shader.VertexUniforms : shader.FragmentUniforms;
            var binding = stage == 0 ? GpuCompiledProgram.VertexUniformBinding : GpuCompiledProgram.FragmentUniformBinding;
            if (block.Length == 0) continue;
            var (buffer, offset) = frame.Stream(block, Math.Max(16, alignment));
            bufferInfos[offsetCount] = new DescriptorBufferInfo(buffer, 0, (ulong)block.Length);
            writes[writeCount++] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = (uint)binding,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.UniformBufferDynamic,
                PBufferInfo = &bufferInfos[offsetCount],
            };
            dynamicOffsets[offsetCount++] = (uint)offset;
        }
        for (var index = 0; index < shader.Textures.Length; index++)
        {
            var texture = shader.Textures[index] is VulkanTexture { IsDisposed: false } t ? t : _empty;
            imageInfos[index] = new DescriptorImageInfo(_samplers[(int)texture.Filter], texture.View, ImageLayout.ShaderReadOnlyOptimal);
            writes[writeCount++] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = set,
                DstBinding = (uint)(GpuCompiledProgram.FirstSamplerBinding + index),
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = &imageInfos[index],
            };
        }
        Vk.UpdateDescriptorSets(Handle, writeCount, writes, 0, null);
        Vk.CmdBindDescriptorSets(commands, PipelineBindPoint.Graphics, objects.PipelineLayout, 0, 1, &set, offsetCount, dynamicOffsets);
        return commands;
    }

    private FrameContext EnsureRecording()
    {
        if (_recording is not null) return _recording;
        var frame = _frames[_frameIndex];
        frame.Wait();
        Collect(frame.Serial);
        frame.Begin();
        _recording = frame;
        return frame;
    }

    private CommandBuffer EnsurePass(VulkanRenderTarget target)
    {
        var frame = EnsureRecording();
        if (!ReferenceEquals(_passTarget, target))
        {
            EndPass();
            BeginPass(target);
        }
        return frame.Commands;
    }

    private void BeginPass(VulkanRenderTarget target)
    {
        if (!target.IsAllocated) throw new InvalidOperationException("Render target has no storage.");
        EndPass();
        var clearColor = target.PendingColor is not null;
        var clearStencil = target.PendingStencil || target.HasDepthStencil && !target.StencilInitialized;
        var initial = clearColor ? ImageLayout.Undefined : target.ColorLayout;
        var pass = RenderPassFor(new PassKey(target.Format, target.Samples, target.HasDepthStencil, clearColor, clearStencil,
            initial, target.RestLayout));
        var clears = stackalloc ClearValue[2];
        var color = target.PendingColor ?? EffectColor.Transparent;
        clears[0] = new ClearValue(new ClearColorValue(color.R, color.G, color.B, color.A));
        clears[1] = new ClearValue(depthStencil: new ClearDepthStencilValue(1, 0));
        var info = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = pass,
            Framebuffer = target.FramebufferFor(RenderPassFor(target.CompatibleKey)),
            RenderArea = new Rect2D(default, new Extent2D((uint)target.Width, (uint)target.Height)),
            ClearValueCount = target.HasDepthStencil ? 2u : 1u,
            PClearValues = clears,
        };
        Vk.CmdBeginRenderPass(_recording!.Commands, &info, SubpassContents.Inline);
        target.PendingColor = null;
        target.PendingStencil = false;
        target.StencilInitialized = true;
        target.ColorLayout = target.RestLayout;
        _passTarget = target;
        _boundPipeline = null;
        _boundViewport = null;
        _boundScissor = null;
    }

    private void EndPass()
    {
        if (_passTarget is null) return;
        Vk.CmdEndRenderPass(_recording!.Commands);
        _passTarget = null;
    }

    internal void Forget(VulkanRenderTarget target)
    {
        if (ReferenceEquals(_passTarget, target)) EndPass();
    }

    private static GpuRect Clip(GpuRect? scissor, VulkanRenderTarget target)
    {
        var s = scissor ?? new GpuRect(0, 0, target.Width, target.Height);
        var x = Math.Clamp(s.X, 0, target.Width);
        var y = Math.Clamp(s.Y, 0, target.Height);
        return new GpuRect(x, y, Math.Clamp(s.X + s.Width, x, target.Width) - x, Math.Clamp(s.Y + s.Height, y, target.Height) - y);
    }

    internal RenderPass RenderPassFor(PassKey key)
    {
        if (_renderPasses.TryGetValue(key, out var pass)) return pass;
        var attachments = stackalloc AttachmentDescription[2];
        attachments[0] = new AttachmentDescription
        {
            Format = key.Format,
            Samples = (SampleCountFlags)key.Samples,
            LoadOp = key.ClearColor ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = key.Initial,
            FinalLayout = key.Final == ImageLayout.Undefined ? ImageLayout.ColorAttachmentOptimal : key.Final,
        };
        attachments[1] = new AttachmentDescription
        {
            Format = DepthStencilFormat,
            Samples = (SampleCountFlags)key.Samples,
            LoadOp = AttachmentLoadOp.DontCare,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = key.ClearStencil ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
            StencilStoreOp = AttachmentStoreOp.Store,
            InitialLayout = key.ClearStencil ? ImageLayout.Undefined : ImageLayout.DepthStencilAttachmentOptimal,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };
        var colorRef = new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);
        var depthRef = new AttachmentReference(1, ImageLayout.DepthStencilAttachmentOptimal);
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorRef,
            PDepthStencilAttachment = key.HasDepthStencil ? &depthRef : null,
        };
        const PipelineStageFlags stages = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.FragmentShaderBit |
            PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit | PipelineStageFlags.TransferBit;
        const AccessFlags access = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit |
            AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit |
            AccessFlags.ShaderReadBit | AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit;
        var dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency(Vk.SubpassExternal, 0, stages, stages, access, access);
        dependencies[1] = new SubpassDependency(0, Vk.SubpassExternal, stages, stages, access, access);
        var info = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = key.HasDepthStencil ? 2u : 1u,
            PAttachments = attachments,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies,
        };
        Check(Vk.CreateRenderPass(Handle, in info, null, out pass), "vkCreateRenderPass");
        _renderPasses.Add(key, pass);
        return pass;
    }

    private Pipeline CreatePipeline(ProgramObjects objects, PipelineKey key)
    {
        var entry = (byte*)SilkMarshal.StringToPtr("main");
        var stages = stackalloc PipelineShaderStageCreateInfo[2];
        stages[0] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit,
            Module = objects.Vertex, PName = entry,
        };
        stages[1] = new PipelineShaderStageCreateInfo
        {
            SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit,
            Module = objects.Fragment, PName = entry,
        };
        var attributeCount = key.Layout?.Attributes.Count ?? 0;
        var attributes = stackalloc VertexInputAttributeDescription[Math.Max(1, attributeCount)];
        for (var index = 0; index < attributeCount; index++)
        {
            var attribute = key.Layout!.Attributes[index];
            attributes[index] = new VertexInputAttributeDescription((uint)attribute.Location, 0,
                attribute.Components switch { 1 => Format.R32Sfloat, 2 => Format.R32G32Sfloat, 3 => Format.R32G32B32Sfloat, _ => Format.R32G32B32A32Sfloat },
                (uint)attribute.Offset);
        }
        var binding = new VertexInputBindingDescription(0, (uint)(key.Layout?.Stride ?? 0), VertexInputRate.Vertex);
        var vertexInput = new PipelineVertexInputStateCreateInfo
        {
            SType = StructureType.PipelineVertexInputStateCreateInfo,
            VertexBindingDescriptionCount = key.Layout is null ? 0u : 1u,
            PVertexBindingDescriptions = &binding,
            VertexAttributeDescriptionCount = (uint)attributeCount,
            PVertexAttributeDescriptions = attributes,
        };
        var assembly = new PipelineInputAssemblyStateCreateInfo
        {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo,
            Topology = key.Topology == GpuTopology.TriangleStrip ? PrimitiveTopology.TriangleStrip : PrimitiveTopology.TriangleList,
        };
        var viewportState = new PipelineViewportStateCreateInfo
        {
            SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1,
        };
        var raster = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            PolygonMode = PolygonMode.Fill, CullMode = CullModeFlags.None, FrontFace = FrontFace.CounterClockwise, LineWidth = 1,
        };
        var multisample = new PipelineMultisampleStateCreateInfo
        {
            SType = StructureType.PipelineMultisampleStateCreateInfo,
            RasterizationSamples = (SampleCountFlags)key.Pass.Samples,
        };
        var face = key.Stencil switch
        {
            GpuStencil.WriteOne => new StencilOpState(StencilOp.Keep, StencilOp.Replace, StencilOp.Keep, CompareOp.Always, 0xFF, 0xFF, 1),
            GpuStencil.EqualOne => new StencilOpState(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep, CompareOp.Equal, 0xFF, 0xFF, 1),
            _ => default,
        };
        var depthStencil = new PipelineDepthStencilStateCreateInfo
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthCompareOp = CompareOp.Always,
            StencilTestEnable = key.Stencil != GpuStencil.Disabled,
            Front = face,
            Back = face,
        };
        var (source, destination) = key.Blend switch
        {
            GpuBlend.Additive => (BlendFactor.One, BlendFactor.One),
            GpuBlend.Screen => (BlendFactor.One, BlendFactor.OneMinusSrcColor),
            GpuBlend.Multiply => (BlendFactor.DstColor, BlendFactor.OneMinusSrcAlpha),
            _ => (BlendFactor.One, BlendFactor.OneMinusSrcAlpha),
        };
        var blendAttachment = new PipelineColorBlendAttachmentState
        {
            BlendEnable = key.Blend != GpuBlend.None,
            SrcColorBlendFactor = source, DstColorBlendFactor = destination, ColorBlendOp = BlendOp.Add,
            SrcAlphaBlendFactor = source, DstAlphaBlendFactor = destination, AlphaBlendOp = BlendOp.Add,
            ColorWriteMask = key.ColorWrite
                ? ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit : 0,
        };
        var blend = new PipelineColorBlendStateCreateInfo
        {
            SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = 1, PAttachments = &blendAttachment,
        };
        var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor, DynamicState.StencilReference };
        var dynamic = new PipelineDynamicStateCreateInfo
        {
            SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 3, PDynamicStates = dynamicStates,
        };
        var info = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo,
            StageCount = 2,
            PStages = stages,
            PVertexInputState = &vertexInput,
            PInputAssemblyState = &assembly,
            PViewportState = &viewportState,
            PRasterizationState = &raster,
            PMultisampleState = &multisample,
            PDepthStencilState = key.Pass.HasDepthStencil ? &depthStencil : null,
            PColorBlendState = &blend,
            PDynamicState = &dynamic,
            Layout = objects.PipelineLayout,
            RenderPass = RenderPassFor(key.Pass),
        };
        try
        {
            Check(Vk.CreateGraphicsPipelines(Handle, default, 1, &info, null, out var pipeline), $"pipeline '{objects.Name}'");
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free((nint)entry);
        }
    }

    // ---- Low-level helpers -------------------------------------------------------------------------------------------

    internal Image CreateImage(int width, int height, Format format, uint levels, SampleCountFlags samples,
        ImageUsageFlags usage, out VulkanMemoryAllocation memory)
    {
        var info = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = levels,
            ArrayLayers = 1,
            Samples = samples,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Check(Vk.CreateImage(Handle, in info, null, out var image), "vkCreateImage");
        Vk.GetImageMemoryRequirements(Handle, image, out var requirements);
        memory = _allocator.AllocateImage(requirements, MemoryPropertyFlags.DeviceLocalBit);
        Check(Vk.BindImageMemory(Handle, image, memory.Memory, memory.Offset), "vkBindImageMemory");
        return image;
    }

    internal ImageView CreateView(Image image, Format format, ImageAspectFlags aspect, uint levels = 1)
    {
        var info = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange(aspect, 0, levels, 0, 1),
        };
        Check(Vk.CreateImageView(Handle, in info, null, out var view), "vkCreateImageView");
        return view;
    }

    internal VkBuffer CreateHostBuffer(ulong size, BufferUsageFlags usage, out VulkanMemoryAllocation memory, out void* mapped)
    {
        var info = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo, Size = size, Usage = usage, SharingMode = SharingMode.Exclusive,
        };
        Check(Vk.CreateBuffer(Handle, in info, null, out var buffer), "vkCreateBuffer");
        Vk.GetBufferMemoryRequirements(Handle, buffer, out var requirements);
        memory = _allocator.AllocateBuffer(requirements, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, map: true);
        Check(Vk.BindBufferMemory(Handle, buffer, memory.Memory, memory.Offset), "vkBindBufferMemory");
        mapped = (void*)memory.Mapped;
        return buffer;
    }

    internal DeviceMemory Allocate(MemoryRequirements requirements, MemoryPropertyFlags flags, void* next = null)
    {
        var info = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            PNext = next,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = MemoryType(requirements.MemoryTypeBits, flags),
        };
        Check(Vk.AllocateMemory(Handle, in info, null, out var memory), "vkAllocateMemory");
        return memory;
    }

    internal uint MemoryType(uint bits, MemoryPropertyFlags flags)
    {
        var properties = MemoryProperties;
        for (var index = 0; index < properties.MemoryTypeCount; index++)
            if ((bits & (1u << index)) != 0 && (properties.MemoryTypes[index].PropertyFlags & flags) == flags)
                return (uint)index;
        throw new NotSupportedException($"No Vulkan memory type has {flags}.");
    }

    internal void Barrier(CommandBuffer commands, Image image, ImageAspectFlags aspect, ImageLayout from, ImageLayout to,
        uint level, uint levels)
    {
        if (from == to && from != ImageLayout.TransferDstOptimal) return;
        const PipelineStageFlags stages = PipelineStageFlags.AllCommandsBit;
        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.MemoryWriteBit,
            DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
            OldLayout = from,
            NewLayout = to,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(aspect, level, levels, 0, 1),
        };
        Vk.CmdPipelineBarrier(commands, stages, stages, 0, 0, null, 0, null, 1, &barrier);
    }

    /// <summary>Destroys GPU objects once no submitted work can still use them.</summary>
    internal void Defer(Action destroy)
    {
        if (IsDisposed) return;
        _garbage.Add((_serial, destroy));
    }

    private void Collect(ulong completed)
    {
        _completedSerial = Math.Max(_completedSerial, completed);
        for (var index = _garbage.Count - 1; index >= 0; index--)
        {
            if (_garbage[index].Serial > _completedSerial) continue;
            _garbage[index].Destroy();
            _garbage.RemoveAt(index);
        }
    }

    private Sampler CreateSampler(Filter filter, bool mipmaps)
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = filter,
            MinFilter = filter,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxLod = mipmaps ? 16 : 0.25f,
        };
        Check(Vk.CreateSampler(Handle, in info, null, out var sampler), "vkCreateSampler");
        return sampler;
    }

    private bool SupportsAttachment(Format format, FormatFeatureFlags feature)
    {
        Vk.GetPhysicalDeviceFormatProperties(PhysicalDevice, format, out var properties);
        return (properties.OptimalTilingFeatures & feature) == feature;
    }

    private static bool Matches(Vk api, PhysicalDevice physical, VulkanDeviceOptions options)
    {
        if (options.DeviceUuid is null && options.DeviceLuid is null) return true;
        var id = new PhysicalDeviceIDProperties { SType = StructureType.PhysicalDeviceIDProperties };
        var properties = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &id };
        api.GetPhysicalDeviceProperties2(physical, &properties);
        if (options.DeviceLuid is { } luid && id.DeviceLuidvalid)
            return new ReadOnlySpan<byte>(id.DeviceLuid, 8).SequenceEqual(luid);
        return options.DeviceUuid is not { } uuid || new ReadOnlySpan<byte>(id.DeviceUuid, 16).SequenceEqual(uuid);
    }

    private static uint? GraphicsFamily(Vk api, PhysicalDevice physical)
    {
        uint count = 0;
        api.GetPhysicalDeviceQueueFamilyProperties(physical, ref count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = families)
            api.GetPhysicalDeviceQueueFamilyProperties(physical, ref count, pointer);
        for (var index = 0u; index < count; index++)
            if (families[index].QueueFlags.HasFlag(QueueFlags.GraphicsBit)) return index;
        return null;
    }

    private static bool InstanceExtensionAvailable(Vk api, string name)
    {
        uint count = 0;
        api.EnumerateInstanceExtensionProperties((byte*)null, ref count, null);
        var extensions = new ExtensionProperties[count];
        fixed (ExtensionProperties* pointer = extensions)
            api.EnumerateInstanceExtensionProperties((byte*)null, ref count, pointer);
        foreach (var extension in extensions)
            if (SilkMarshal.PtrToString((nint)extension.ExtensionName) == name) return true;
        return false;
    }

    private static bool DeviceExtensionAvailable(Vk api, PhysicalDevice physical, string name)
    {
        uint count = 0;
        api.EnumerateDeviceExtensionProperties(physical, (byte*)null, ref count, null);
        var extensions = new ExtensionProperties[count];
        fixed (ExtensionProperties* pointer = extensions)
            api.EnumerateDeviceExtensionProperties(physical, (byte*)null, ref count, pointer);
        foreach (var extension in extensions)
            if (SilkMarshal.PtrToString((nint)extension.ExtensionName) == name) return true;
        return false;
    }

    internal static void Check(Result result, string what)
    {
        if (result != Result.Success) throw new InvalidOperationException($"Vulkan {what} failed: {result}.");
    }

    public override void Abandon()
    {
        base.Abandon();
        _recording = null;
        _passTarget = null;
    }

    protected override void DisposeCore()
    {
        Submit(wait: true);
        Vk.DeviceWaitIdle(Handle);
        Collect(ulong.MaxValue);
        _empty.Dispose();
        Collect(ulong.MaxValue);
        foreach (var frame in _frames) frame.Dispose();
        _allocator.Dispose();
        foreach (var pipeline in _pipelines.Values) Vk.DestroyPipeline(Handle, pipeline, null);
        foreach (var pass in _renderPasses.Values) Vk.DestroyRenderPass(Handle, pass, null);
        foreach (var program in _programs.Values) program.Dispose();
        foreach (var sampler in _samplers) Vk.DestroySampler(Handle, sampler, null);
        Vk.DestroyDevice(Handle, null);
        Vk.DestroyInstance(Instance, null);
    }

    // ---- Nested types ------------------------------------------------------------------------------------------------

    internal readonly record struct PassKey(Format Format, int Samples, bool HasDepthStencil, bool ClearColor, bool ClearStencil,
        ImageLayout Initial, ImageLayout Final);

    private readonly record struct PipelineKey(GpuCompiledProgram Program, GpuVertexLayout? Layout, GpuBlend Blend,
        bool ColorWrite, GpuStencil Stencil, GpuTopology Topology, PassKey Pass)
    {
        public bool Equals(PipelineKey other) => ReferenceEquals(Program, other.Program) && ReferenceEquals(Layout, other.Layout) &&
            Blend == other.Blend && ColorWrite == other.ColorWrite && Stencil == other.Stencil && Topology == other.Topology &&
            Pass == other.Pass;
        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Program),
            RuntimeHelpers.GetHashCode(Layout), Blend, ColorWrite, Stencil, Topology, Pass);
    }

    /// <summary>Shader modules, set layout and pipeline layout of one compiled program.</summary>
    private sealed class ProgramObjects
    {
        private readonly VulkanGpuDevice _device;

        public ProgramObjects(VulkanGpuDevice device, GpuCompiledProgram program)
        {
            _device = device;
            Name = program.Name;
            Vertex = Module(program.Vertex.Spirv);
            Fragment = Module(program.Fragment.Spirv);
            var bindings = new List<DescriptorSetLayoutBinding>();
            if (program.Vertex.Uniforms is not null)
                bindings.Add(new DescriptorSetLayoutBinding(GpuCompiledProgram.VertexUniformBinding,
                    DescriptorType.UniformBufferDynamic, 1, ShaderStageFlags.VertexBit));
            if (program.Fragment.Uniforms is not null)
                bindings.Add(new DescriptorSetLayoutBinding(GpuCompiledProgram.FragmentUniformBinding,
                    DescriptorType.UniformBufferDynamic, 1, ShaderStageFlags.FragmentBit));
            for (var index = 0; index < program.Samplers.Count; index++)
                bindings.Add(new DescriptorSetLayoutBinding((uint)(GpuCompiledProgram.FirstSamplerBinding + index),
                    DescriptorType.CombinedImageSampler, 1, ShaderStageFlags.FragmentBit | ShaderStageFlags.VertexBit));
            var array = bindings.ToArray();
            fixed (DescriptorSetLayoutBinding* pointer = array)
            {
                var info = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = (uint)array.Length, PBindings = pointer,
                };
                Check(device.Vk.CreateDescriptorSetLayout(device.Handle, in info, null, out var layout), "set layout");
                SetLayout = layout;
            }
            var setLayout = SetLayout;
            var pipelineInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &setLayout,
            };
            Check(device.Vk.CreatePipelineLayout(device.Handle, in pipelineInfo, null, out var pipelineLayout), "pipeline layout");
            PipelineLayout = pipelineLayout;
        }

        public string Name { get; }
        public ShaderModule Vertex { get; }
        public ShaderModule Fragment { get; }
        public DescriptorSetLayout SetLayout { get; }
        public PipelineLayout PipelineLayout { get; }

        private ShaderModule Module(byte[] spirv)
        {
            fixed (byte* code = spirv)
            {
                var info = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)spirv.Length, PCode = (uint*)code,
                };
                Check(_device.Vk.CreateShaderModule(_device.Handle, in info, null, out var module), $"shader module '{Name}'");
                return module;
            }
        }

        public void Dispose()
        {
            var vk = _device.Vk;
            vk.DestroyShaderModule(_device.Handle, Vertex, null);
            vk.DestroyShaderModule(_device.Handle, Fragment, null);
            vk.DestroyPipelineLayout(_device.Handle, PipelineLayout, null);
            vk.DestroyDescriptorSetLayout(_device.Handle, SetLayout, null);
        }
    }

    /// <summary>Per-frame command buffers, fence, descriptor pools and a host-visible streaming arena.</summary>
    private sealed class FrameContext
    {
        private const int ChunkSize = 512 * 1024;
        private readonly VulkanGpuDevice _device;
        private readonly CommandPool _pool;
        private readonly Fence _fence;
        private readonly List<DescriptorPool> _descriptorPools = [];
        private readonly List<(VkBuffer Buffer, VulkanMemoryAllocation Memory, nint Mapped, int Size)> _chunks = [];
        private readonly Ledger _ledger = new();
        private int _descriptorPool;
        private int _chunk;
        private int _offset;
        private bool _submitted;
        private bool _uploadsBegun;

        public FrameContext(VulkanGpuDevice device)
        {
            _device = device;
            var vk = device.Vk;
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = device.QueueFamily,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            };
            Check(vk.CreateCommandPool(device.Handle, in poolInfo, null, out _pool), "command pool");
            var allocate = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo, CommandPool = _pool, Level = CommandBufferLevel.Primary,
                CommandBufferCount = 2,
            };
            var buffers = stackalloc CommandBuffer[2];
            Check(vk.AllocateCommandBuffers(device.Handle, in allocate, buffers), "command buffers");
            Commands = buffers[0];
            UploadCommands = buffers[1];
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            Check(vk.CreateFence(device.Handle, in fenceInfo, null, out _fence), "fence");
        }

        public CommandBuffer Commands { get; }
        public CommandBuffer UploadCommands { get; }
        public ulong Serial { get; private set; }
        public long ArenaBytes => _chunks.Sum(chunk => (long)chunk.Size);

        public void Wait()
        {
            if (!_submitted) return;
            var fence = _fence;
            _device.Vk.WaitForFences(_device.Handle, 1, &fence, true, ulong.MaxValue);
            _submitted = false;
        }

        public void Begin()
        {
            var vk = _device.Vk;
            var fence = _fence;
            vk.ResetFences(_device.Handle, 1, &fence);
            // Release any overflow chunks or descriptor pools from a previous spike now that this frame's fence completed.
            if (_chunks.Count > 1) ReleaseChunks(keepCount: 1);
            if (_descriptorPools.Count > 1)
            {
                for (var index = _descriptorPools.Count - 1; index >= 1; index--)
                {
                    vk.DestroyDescriptorPool(_device.Handle, _descriptorPools[index], null);
                    _descriptorPools.RemoveAt(index);
                }
            }
            foreach (var pool in _descriptorPools) vk.ResetDescriptorPool(_device.Handle, pool, 0);
            _descriptorPool = 0;
            _chunk = 0;
            _offset = 0;
            _uploadsBegun = false;
            var begin = new CommandBufferBeginInfo
            {
                SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            vk.BeginCommandBuffer(Commands, in begin);
        }

        /// <summary>Releases all streaming chunks and extra descriptor pools while the device is idle.</summary>
        public void Trim()
        {
            if (_chunks.Count > 0) ReleaseChunks(keepCount: 0);
            if (_descriptorPools.Count > 0)
            {
                var vk = _device.Vk;
                foreach (var pool in _descriptorPools) vk.DestroyDescriptorPool(_device.Handle, pool, null);
                _descriptorPools.Clear();
            }
            _descriptorPool = 0;
            _chunk = 0;
            _offset = 0;
        }

        private void ReleaseChunks(int keepCount)
        {
            var vk = _device.Vk;
            for (var index = _chunks.Count - 1; index >= keepCount; index--)
            {
                var chunk = _chunks[index];
                vk.DestroyBuffer(_device.Handle, chunk.Buffer, null);
                _device._allocator.Free(chunk.Memory);
                _chunks.RemoveAt(index);
            }
            if (_chunks.Count == 0) _device.Resources.ReleaseBuffer(_ledger.Id);
            else _device.Resources.Buffer(_ledger.Id, _chunks.Sum(chunk => (long)chunk.Size));
        }

        /// <summary>The upload command buffer, submitted before <see cref="Commands"/>.</summary>
        public CommandBuffer Uploads()
        {
            if (!_uploadsBegun)
            {
                var begin = new CommandBufferBeginInfo
                {
                    SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
                };
                _device.Vk.BeginCommandBuffer(UploadCommands, in begin);
                _uploadsBegun = true;
            }
            return UploadCommands;
        }

        public void Submit(List<VkSemaphore> waits, List<VkSemaphore> signals, ulong serial)
        {
            var vk = _device.Vk;
            vk.EndCommandBuffer(Commands);
            if (_uploadsBegun) vk.EndCommandBuffer(UploadCommands);
            var buffers = stackalloc CommandBuffer[2];
            var count = 0u;
            if (_uploadsBegun) buffers[count++] = UploadCommands;
            buffers[count++] = Commands;
            var waitArray = waits.ToArray();
            var signalArray = signals.ToArray();
            var stages = stackalloc PipelineStageFlags[Math.Max(1, waitArray.Length)];
            for (var index = 0; index < waitArray.Length; index++) stages[index] = PipelineStageFlags.AllCommandsBit;
            fixed (VkSemaphore* waitPointer = waitArray)
            fixed (VkSemaphore* signalPointer = signalArray)
            {
                var info = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    WaitSemaphoreCount = (uint)waitArray.Length,
                    PWaitSemaphores = waitPointer,
                    PWaitDstStageMask = stages,
                    CommandBufferCount = count,
                    PCommandBuffers = buffers,
                    SignalSemaphoreCount = (uint)signalArray.Length,
                    PSignalSemaphores = signalPointer,
                };
                Check(vk.QueueSubmit(_device.Queue, 1, &info, _fence), "vkQueueSubmit");
            }
            Serial = serial;
            _submitted = true;
        }

        public DescriptorSet AllocateSet(DescriptorSetLayout layout)
        {
            var vk = _device.Vk;
            while (true)
            {
                if (_descriptorPool == _descriptorPools.Count) _descriptorPools.Add(CreateDescriptorPool());
                var pool = _descriptorPools[_descriptorPool];
                var info = new DescriptorSetAllocateInfo
                {
                    SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = pool, DescriptorSetCount = 1,
                    PSetLayouts = &layout,
                };
                DescriptorSet set;
                var result = vk.AllocateDescriptorSets(_device.Handle, &info, &set);
                if (result == Result.Success) return set;
                if (result is not (Result.ErrorOutOfPoolMemory or Result.ErrorFragmentedPool))
                    Check(result, "vkAllocateDescriptorSets");
                _descriptorPool++;
            }
        }

        /// <summary>Copies <paramref name="data"/> into this frame's arena.</summary>
        public (VkBuffer Buffer, int Offset) Stream(ReadOnlySpan<byte> data, int alignment)
        {
            var span = Reserve(data.Length, alignment, out var buffer, out var offset);
            data.CopyTo(span);
            return (buffer, offset);
        }

        public Span<byte> Staging(int length, out int offset, out VkBuffer buffer) => Reserve(length, 16, out buffer, out offset);

        private Span<byte> Reserve(int length, int alignment, out VkBuffer buffer, out int offset)
        {
            length = Math.Max(4, length);
            while (true)
            {
                if (_chunk == _chunks.Count) AddChunk(Math.Max(ChunkSize, length));
                var chunk = _chunks[_chunk];
                var aligned = (_offset + alignment - 1) / alignment * alignment;
                if (aligned + length <= chunk.Size)
                {
                    _offset = aligned + length;
                    buffer = chunk.Buffer;
                    offset = aligned;
                    return new Span<byte>((byte*)chunk.Mapped + aligned, length);
                }
                _chunk++;
                _offset = 0;
            }
        }

        private void AddChunk(int size)
        {
            var buffer = _device.CreateHostBuffer((ulong)size,
                BufferUsageFlags.UniformBufferBit | BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferSrcBit,
                out var memory, out var mapped);
            _chunks.Add((buffer, memory, (nint)mapped, size));
            _device.Resources.Buffer(_ledger.Id, _chunks.Sum(chunk => (long)chunk.Size));
        }

        private DescriptorPool CreateDescriptorPool()
        {
            var sizes = stackalloc DescriptorPoolSize[2];
            sizes[0] = new DescriptorPoolSize(DescriptorType.UniformBufferDynamic, 2048);
            sizes[1] = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 4096);
            var info = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1024, PoolSizeCount = 2, PPoolSizes = sizes,
            };
            Check(_device.Vk.CreateDescriptorPool(_device.Handle, in info, null, out var pool), "descriptor pool");
            return pool;
        }

        public void Dispose()
        {
            var vk = _device.Vk;
            Trim();
            vk.DestroyFence(_device.Handle, _fence, null);
            vk.DestroyCommandPool(_device.Handle, _pool, null);
        }

        private sealed class Ledger : GpuResource
        {
            protected override void DisposeCore() { }
        }
    }
}

internal sealed unsafe class VulkanTexture : GpuTexture
{
    private readonly VulkanGpuDevice? _device;
    private readonly VulkanMemoryAllocation _memory;
    private readonly Image _image;

    public VulkanTexture(VulkanGpuDevice? device, Image image, VulkanMemoryAllocation memory, ImageView view, int width, int height,
        GpuFilter filter)
    {
        _device = device;
        _image = image;
        _memory = memory;
        View = view;
        Width = width;
        Height = height;
        Filter = filter;
    }

    public ImageView View { get; }

    protected override void DisposeCore()
    {
        if (_device is not { } device) return;
        var (image, memory, view) = (_image, _memory, View);
        device.Defer(() =>
        {
            device.Vk.DestroyImageView(device.Handle, view, null);
            device.Vk.DestroyImage(device.Handle, image, null);
            device.MemoryAllocator.Free(memory);
        });
    }
}

internal sealed unsafe class VulkanBuffer(VulkanGpuDevice device, VkBuffer handle, VulkanMemoryAllocation memory, long size) : GpuBuffer
{
    public VkBuffer Handle { get; } = handle;
    public long Length { get; } = size;

    protected override void DisposeCore()
    {
        var (buffer, deviceMemory) = (Handle, memory);
        device.Resources.ReleaseBuffer(Id);
        device.Defer(() =>
        {
            device.Vk.DestroyBuffer(device.Handle, buffer, null);
            device.MemoryAllocator.Free(deviceMemory);
        });
    }
}

internal sealed class VulkanShader(GpuCompiledProgram program) : GpuBlockShader(program)
{
    protected override void DisposeCore() { }
}

internal sealed unsafe class VulkanRenderTarget : GpuRenderTarget
{
    private readonly VulkanGpuDevice _device;
    private readonly bool _external;
    private readonly bool _allowLazyDepthStencil;
    private VulkanMemoryAllocation _colorMemory;
    private VulkanMemoryAllocation _depthMemory;
    private ImageView _colorView;
    private Image _depthImage;
    private ImageView _depthView;
    private Framebuffer _framebuffer;
    private VulkanTexture? _texture;
    private int _requestedSamples;

    public VulkanRenderTarget(VulkanGpuDevice device, bool depthStencil)
    {
        _device = device;
        HasDepthStencil = depthStencil;
        Format = VulkanGpuDevice.ColorFormat;
    }

    private VulkanRenderTarget(VulkanGpuDevice device, Image image, Format format, int width, int height, ImageLayout restLayout)
    {
        _device = device;
        _external = true;
        _allowLazyDepthStencil = true;
        // Allocate the depth/stencil attachment lazily: when post-processing is active, swapchain targets never use stencil.
        HasDepthStencil = false;
        Format = format;
        ColorImage = image;
        RestLayout = restLayout;
        ColorLayout = ImageLayout.Undefined;
        Width = width;
        Height = height;
        _colorView = device.CreateView(image, format, ImageAspectFlags.ColorBit);
        _texture = new VulkanTexture(null, image, default, _colorView, width, height, GpuFilter.Linear);
    }

    public static VulkanRenderTarget External(VulkanGpuDevice device, Image image, Format format, int width, int height,
        ImageLayout restLayout) => new(device, image, format, width, height, restLayout);

    public Format Format { get; }
    public Image ColorImage { get; private set; }
    /// <summary>The layout the color image is in outside render passes and copies.</summary>
    public ImageLayout RestLayout { get; private set; } = ImageLayout.ShaderReadOnlyOptimal;
    public ImageLayout ColorLayout { get; internal set; } = ImageLayout.Undefined;
    public override GpuTexture? Texture => Samples == 1 ? _texture : null;
    internal EffectColor? PendingColor { get; set; }
    internal bool PendingStencil { get; set; }
    internal bool StencilInitialized { get; set; }

    internal VulkanGpuDevice.PassKey CompatibleKey =>
        new(Format, Samples, HasDepthStencil, false, false, ImageLayout.Undefined, ImageLayout.Undefined);

    /// <summary>Lazily creates the depth/stencil attachment for an external target on first stencil use.</summary>
    internal void EnsureDepthStencil()
    {
        if (HasDepthStencil || !_allowLazyDepthStencil || !IsAllocated) return;
        _device.Forget(this);
        if (_framebuffer.Handle != 0)
        {
            var device = _device;
            var framebuffer = _framebuffer;
            device.Defer(() => device.Vk.DestroyFramebuffer(device.Handle, framebuffer, null));
            _framebuffer = default;
        }
        HasDepthStencil = true;
        CreateDepth(Width, Height, (SampleCountFlags)Samples);
    }

    public override void EnsureSize(int width, int height, int samples = 1)
    {
        if (_external) throw new InvalidOperationException("An external target cannot be resized.");
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        samples = Math.Clamp(samples, 1, _device.MaxSamples);
        if (samples > 1 && !HasDepthStencil)
            throw new InvalidOperationException("Multisampled targets carry a depth/stencil attachment.");
        if (width == Width && height == Height && samples == _requestedSamples && ColorImage.Handle != 0) return;
        Release();
        var sampleFlags = (SampleCountFlags)samples;
        var usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit;
        if (samples == 1) usage |= ImageUsageFlags.SampledBit;
        ColorImage = _device.CreateImage(width, height, Format, 1, sampleFlags, usage, out _colorMemory);
        _colorView = _device.CreateView(ColorImage, Format, ImageAspectFlags.ColorBit);
        RestLayout = samples == 1 ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.ColorAttachmentOptimal;
        ColorLayout = ImageLayout.Undefined;
        if (HasDepthStencil) CreateDepth(width, height, sampleFlags);
        Width = width;
        Height = height;
        Samples = samples;
        _requestedSamples = samples;
        if (samples == 1) _texture = new VulkanTexture(null, ColorImage, default, _colorView, width, height, GpuFilter.Linear);
        _device.Resources.Target(Id, (long)width * height * (HasDepthStencil ? 8 : 4) * samples);
    }

    internal Framebuffer FramebufferFor(RenderPass compatible)
    {
        if (_framebuffer.Handle != 0) return _framebuffer;
        var views = stackalloc ImageView[] { _colorView, _depthView };
        var info = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = compatible,
            AttachmentCount = HasDepthStencil ? 2u : 1u,
            PAttachments = views,
            Width = (uint)Width,
            Height = (uint)Height,
            Layers = 1,
        };
        VulkanGpuDevice.Check(_device.Vk.CreateFramebuffer(_device.Handle, in info, null, out _framebuffer), "vkCreateFramebuffer");
        return _framebuffer;
    }

    private void CreateDepth(int width, int height, SampleCountFlags samples)
    {
        _depthImage = _device.CreateImage(width, height, _device.DepthStencilFormat, 1, samples,
            ImageUsageFlags.DepthStencilAttachmentBit, out _depthMemory);
        _depthView = _device.CreateView(_depthImage, _device.DepthStencilFormat,
            ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit);
        StencilInitialized = false;
    }

    public override void Release()
    {
        _device.Forget(this);
        var device = _device;
        var (color, colorMemory, colorView, depth, depthMemory, depthView, framebuffer) =
            (_external ? default : ColorImage, _colorMemory, _colorView, _depthImage, _depthMemory, _depthView, _framebuffer);
        device.Defer(() =>
        {
            var vk = device.Vk;
            if (framebuffer.Handle != 0) vk.DestroyFramebuffer(device.Handle, framebuffer, null);
            if (colorView.Handle != 0) vk.DestroyImageView(device.Handle, colorView, null);
            if (color.Handle != 0) vk.DestroyImage(device.Handle, color, null);
            if (colorMemory.IsValid) device.MemoryAllocator.Free(colorMemory);
            if (depthView.Handle != 0) vk.DestroyImageView(device.Handle, depthView, null);
            if (depth.Handle != 0) vk.DestroyImage(device.Handle, depth, null);
            if (depthMemory.IsValid) device.MemoryAllocator.Free(depthMemory);
        });
        _texture?.Abandon();
        _texture = null;
        _framebuffer = default;
        _colorView = _depthView = default;
        _depthImage = default;
        _colorMemory = _depthMemory = default;
        if (_external) return;
        _device.Resources.ReleaseTarget(Id);
        ColorImage = default;
        Width = Height = 0;
        Samples = 1;
        _requestedSamples = 0;
        PendingColor = null;
        PendingStencil = false;
    }
}
