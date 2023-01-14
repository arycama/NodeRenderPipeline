public struct DrawIndexedInstancedIndirectArgs
{
    public uint indexCountPerInstance;
    public uint instanceCount;
    public uint startIndexLocation;
    public int baseVertexLocation;
    public uint startInstanceLocation;

    public DrawIndexedInstancedIndirectArgs(uint indexCountPerInstance, uint instanceCount, uint startIndexLocation, int baseVertexLocation, uint startInstanceLocation)
    {
        this.indexCountPerInstance = indexCountPerInstance;
        this.instanceCount = instanceCount;
        this.startIndexLocation = startIndexLocation;
        this.baseVertexLocation = baseVertexLocation;
        this.startInstanceLocation = startInstanceLocation;
    }
}
