
// Data for a specific lod of an instance type
public struct InstanceTypeLodData
{
    public int rendererStart, rendererCount, instancesStart, pad;

    public InstanceTypeLodData(int rendererStart, int rendererCount, int instancesStart)
    {
        this.rendererStart = rendererStart;
        this.rendererCount = rendererCount;
        this.instancesStart = instancesStart;
        pad = 0;
    }
}
