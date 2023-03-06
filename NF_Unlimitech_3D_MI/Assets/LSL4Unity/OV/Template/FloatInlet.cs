namespace LSL4Unity.OV.Template
{
/// <summary> Just an example implementation for a Inlet receiving Float values for OpenViBE Link. </summary>
/// <seealso cref="OVFloatInlet" />
public class FloatInlet : OVFloatInlet
{
	public float[] LastSample { get; private set; }

	/// <inheritdoc cref="OVFloatInlet.Process"/>
	protected override void Process(float[] input, double time) { LastSample = input; }
}
}
