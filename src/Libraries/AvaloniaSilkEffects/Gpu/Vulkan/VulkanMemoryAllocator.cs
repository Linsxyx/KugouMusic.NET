using Silk.NET.Vulkan;

namespace AvaloniaSilkEffects.Gpu.Vulkan;

/// <summary>A Vulkan device-memory slice, either sub-allocated from a pooled block or dedicated.</summary>
internal readonly struct VulkanMemoryAllocation
{
    internal VulkanMemoryAllocation(DeviceMemory memory, ulong offset, ulong size, nint mapped,
        VulkanMemoryAllocator.PoolBlock? block)
    {
        Memory = memory;
        Offset = offset;
        Size = size;
        Mapped = mapped;
        Block = block;
    }

    public DeviceMemory Memory { get; }
    public ulong Offset { get; }
    public ulong Size { get; }
    public nint Mapped { get; }
    internal VulkanMemoryAllocator.PoolBlock? Block { get; }
    public bool IsValid => Memory.Handle != 0;
}

/// <summary>
/// Simple block sub-allocator for small Vulkan images and host-visible buffers. Keeps images and buffers in separate
/// pools per memory type so optimal-tiling images never alias buffer granularity, avoids per-texture WDDM page-alignment
/// overhead, and immediately returns empty blocks to Vulkan.
/// </summary>
internal sealed unsafe class VulkanMemoryAllocator
{
    internal const ulong ImageBlockSize = 4UL * 1024 * 1024;
    internal const ulong ImageMaxPooledSize = ImageBlockSize / 2;
    internal const ulong BufferBlockSize = 2UL * 1024 * 1024;
    internal const ulong BufferMaxPooledSize = BufferBlockSize / 2;

    private readonly VulkanGpuDevice _device;
    private readonly Dictionary<(bool IsImage, uint MemoryType), List<PoolBlock>> _pools = [];

    public VulkanMemoryAllocator(VulkanGpuDevice device) => _device = device;

    /// <summary>Number of currently live pooled blocks (for diagnostics and tests).</summary>
    internal int PooledBlockCount => _pools.Values.Sum(list => list.Count);

    public VulkanMemoryAllocation AllocateImage(MemoryRequirements requirements, MemoryPropertyFlags flags, void* next = null) =>
        AllocateCore(requirements, flags, isImage: true, map: false, next);

    public VulkanMemoryAllocation AllocateBuffer(MemoryRequirements requirements, MemoryPropertyFlags flags, bool map, void* next = null) =>
        AllocateCore(requirements, flags, isImage: false, map, next);

    private VulkanMemoryAllocation AllocateCore(MemoryRequirements requirements, MemoryPropertyFlags flags, bool isImage,
        bool map, void* next)
    {
        var memoryType = _device.MemoryType(requirements.MemoryTypeBits, flags);
        var maxPooled = isImage ? ImageMaxPooledSize : BufferMaxPooledSize;
        var blockSize = isImage ? ImageBlockSize : BufferBlockSize;

        if (next is not null || requirements.Size == 0 || requirements.Size > maxPooled)
            return AllocateDedicated(requirements.Size, memoryType, map, next);

        var alignment = Math.Max(1UL, requirements.Alignment);
        var key = (isImage, memoryType);
        if (!_pools.TryGetValue(key, out var blocks))
        {
            blocks = [];
            _pools.Add(key, blocks);
        }

        foreach (var block in blocks)
            if (block.TryAllocate(requirements.Size, alignment, out var offset))
                return new VulkanMemoryAllocation(block.Memory, offset, requirements.Size,
                    block.Mapped != 0 ? (nint)((byte*)block.Mapped + offset) : 0, block);

        var created = CreateBlock(blockSize, memoryType, map, key);
        blocks.Add(created);
        if (!created.TryAllocate(requirements.Size, alignment, out var createdOffset))
            throw new InvalidOperationException($"Failed to sub-allocate {requirements.Size} bytes from a {blockSize}-byte block.");
        return new VulkanMemoryAllocation(created.Memory, createdOffset, requirements.Size,
            created.Mapped != 0 ? (nint)((byte*)created.Mapped + createdOffset) : 0, created);
    }

    public void Free(VulkanMemoryAllocation allocation)
    {
        if (!allocation.IsValid) return;
        if (allocation.Block is null)
        {
            _device.Vk.FreeMemory(_device.Handle, allocation.Memory, null);
            return;
        }
        var block = allocation.Block;
        block.Free(allocation.Offset, allocation.Size);
        if (block.LiveCount == 0)
        {
            if (_pools.TryGetValue(block.PoolKey, out var blocks))
                blocks.Remove(block);
            _device.Vk.FreeMemory(_device.Handle, block.Memory, null);
        }
    }

    public void Dispose()
    {
        foreach (var blocks in _pools.Values)
        {
            foreach (var block in blocks)
                _device.Vk.FreeMemory(_device.Handle, block.Memory, null);
            blocks.Clear();
        }
        _pools.Clear();
    }

    private VulkanMemoryAllocation AllocateDedicated(ulong size, uint memoryType, bool map, void* next)
    {
        var info = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            PNext = next,
            AllocationSize = size,
            MemoryTypeIndex = memoryType,
        };
        VulkanGpuDevice.Check(_device.Vk.AllocateMemory(_device.Handle, in info, null, out var memory), "vkAllocateMemory");
        nint mapped = 0;
        if (map)
        {
            void* pointer;
            VulkanGpuDevice.Check(_device.Vk.MapMemory(_device.Handle, memory, 0, size, 0, &pointer), "vkMapMemory");
            mapped = (nint)pointer;
        }
        return new VulkanMemoryAllocation(memory, 0, size, mapped, null);
    }

    private PoolBlock CreateBlock(ulong size, uint memoryType, bool map, (bool IsImage, uint MemoryType) key)
    {
        var info = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryType,
        };
        VulkanGpuDevice.Check(_device.Vk.AllocateMemory(_device.Handle, in info, null, out var memory), "vkAllocateMemory");
        nint mapped = 0;
        if (map)
        {
            void* pointer;
            VulkanGpuDevice.Check(_device.Vk.MapMemory(_device.Handle, memory, 0, size, 0, &pointer), "vkMapMemory");
            mapped = (nint)pointer;
        }
        return new PoolBlock(memory, size, mapped, key);
    }

    /// <summary>Sorted free-list allocator over a single contiguous memory block.</summary>
    internal sealed class PoolBlock
    {
        private readonly List<(ulong Offset, ulong Size)> _free = [];

        public PoolBlock(DeviceMemory memory, ulong size, nint mapped, (bool IsImage, uint MemoryType) poolKey)
        {
            Memory = memory;
            Size = size;
            Mapped = mapped;
            PoolKey = poolKey;
            _free.Add((0, size));
        }

        public DeviceMemory Memory { get; }
        public ulong Size { get; }
        public nint Mapped { get; }
        public (bool IsImage, uint MemoryType) PoolKey { get; }
        public int LiveCount { get; private set; }
        internal int FreeRangeCount => _free.Count;

        public bool TryAllocate(ulong size, ulong alignment, out ulong offset)
        {
            for (var index = 0; index < _free.Count; index++)
            {
                var range = _free[index];
                var aligned = AlignUp(range.Offset, alignment);
                var rangeEnd = range.Offset + range.Size;
                if (aligned + size > rangeEnd) continue;

                var leading = aligned - range.Offset;
                var trailingStart = aligned + size;
                var trailing = rangeEnd - trailingStart;

                if (leading > 0 && trailing > 0)
                {
                    _free[index] = (range.Offset, leading);
                    _free.Insert(index + 1, (trailingStart, trailing));
                }
                else if (leading > 0)
                {
                    _free[index] = (range.Offset, leading);
                }
                else if (trailing > 0)
                {
                    _free[index] = (trailingStart, trailing);
                }
                else
                {
                    _free.RemoveAt(index);
                }

                LiveCount++;
                offset = aligned;
                return true;
            }
            offset = 0;
            return false;
        }

        public void Free(ulong offset, ulong size)
        {
            LiveCount--;
            var insertAt = 0;
            while (insertAt < _free.Count && _free[insertAt].Offset < offset)
                insertAt++;
            _free.Insert(insertAt, (offset, size));

            // Coalesce with the next range.
            if (insertAt + 1 < _free.Count && _free[insertAt].Offset + _free[insertAt].Size == _free[insertAt + 1].Offset)
            {
                _free[insertAt] = (_free[insertAt].Offset, _free[insertAt].Size + _free[insertAt + 1].Size);
                _free.RemoveAt(insertAt + 1);
            }
            // Coalesce with the previous range.
            if (insertAt > 0 && _free[insertAt - 1].Offset + _free[insertAt - 1].Size == _free[insertAt].Offset)
            {
                _free[insertAt - 1] = (_free[insertAt - 1].Offset, _free[insertAt - 1].Size + _free[insertAt].Size);
                _free.RemoveAt(insertAt);
            }
        }

        private static ulong AlignUp(ulong value, ulong alignment) =>
            alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;
    }
}
