namespace LSL4Unity.OV.Template
{
/// <summary> Just an example implementation for a Inlet receiving double values for OpenViBE Link. </summary>
/// <seealso cref="OVFloatInlet" />
public class DoubleInlet : OVDoubleInlet
{
	public double[] LastSample { get; private set; }

	/// <inheritdoc cref="OVDoubleInlet.Process"/>
	protected override void Process(double[] input, double time) { LastSample = input; }
}
}
