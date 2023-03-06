using System;
using UnityEngine;
using static LSL.liblsl;
using LSL4Unity;


namespace LSL4Unity.OV
{
/// <summary> Base Inlet for OpenViBE Link. </summary>
/// <seealso cref="MonoBehaviour" />
public abstract class OVOutlet<T> : MonoBehaviour
{
	
	private enum UpdateMoment { FixedUpdate, Update, OnDemand }

	protected enum StreamTypes { /* Matrix, Stimulation,*/ Double, Float, Int }

	[SerializeField] private UpdateMoment moment       = UpdateMoment.OnDemand;
	[SerializeField] private string       streamName   = "ovSignal";
	[SerializeField] private int          channelCount = 1;

	public string StreamName   => streamName;
	public int    ChannelCount => channelCount;

	protected StreamOutlet outlet;
	protected StreamInfo   info;
	protected T[]          samples;

	protected StreamTypes streamType = StreamTypes.Int;

	private new string GetType()
	{
		switch (streamType) {
			//case StreamTypes.Matrix:      return "";
			//case StreamTypes.Stimulation: return "Markers";
			case StreamTypes.Double: return "signal";
			case StreamTypes.Float:  return "signal";
			case StreamTypes.Int:    return "Markers";
			default:                 throw new ArgumentOutOfRangeException();
		}
	}

	private ChannelFormat GetFormat()
	{
		switch (streamType) {
			case StreamTypes.Double: return ChannelFormat.Double64;
			case StreamTypes.Float:  return ChannelFormat.Float32;
			case StreamTypes.Int:    return ChannelFormat.Int32;
			default:                 throw new ArgumentOutOfRangeException();
		}
	}


	/// <summary> Start is called before the first frame update. </summary>
	private void Start()
	{
		samples = new T[channelCount];
		info    = new StreamInfo(streamName, GetType(), channelCount, LSL.IRREGULAR_RATE, GetFormat());
		outlet  = new StreamOutlet(info);
		Debug.Log($"Creating Stream : Name = {info.Name()}, Type = {info.Type()}, Channel Count = {info.ChannelCount()}, Format = {info.ChannelFormat()}");
	}

	/// <summary> Fixupdate is called once per physics framerate. </summary>
	private void FixedUpdate() { if (moment == UpdateMoment.FixedUpdate && outlet != null) { PushSamples(); } }

	/// <summary> Update is called once per frame. </summary>
	private void Update() { if (moment == UpdateMoment.Update && outlet != null) { PushSamples(); } }

	/// <summary> ForceUpdate is called when it's needed. </summary>
	/// <param name="input"> The samples to push. </param>
	public void ForceUpdate(T[] input)
	{
		if (outlet != null) {
			Process(input);
			PushSamples();
		}
	}

	/// <summary> Push the samples. </summary>
	protected abstract void PushSamples();

	/// <summary> Override this method in the subclass to specify what should happen when samples are available. </summary>
	/// <param name="input"> The Incomming Sample. </param>
	protected abstract void Process(T[] input);
}

/// <summary> Float Inlet for OpenViBE Link. </summary>
/// <seealso cref="OVOutlet{T}" />
public abstract class OVFloatOutlet : OVOutlet<float>
{
	protected OVFloatOutlet() { streamType = StreamTypes.Float; }

	/// <inheritdoc cref="OVOutlet{T}.PushSamples"/>
	protected override void PushSamples() { outlet.PushSample(samples); }
}

/// <summary> Double Inlet for OpenViBE Link. </summary>
/// <seealso cref="OVOutlet{T}" />
public abstract class OVDoubleOutlet : OVOutlet<double>
{
	protected OVDoubleOutlet() { streamType = StreamTypes.Double; }

	/// <inheritdoc cref="OVOutlet{T}.PushSamples"/>
	protected override void PushSamples() { outlet.PushSample(samples); }
}

/// <summary> Int Inlet for OpenViBE Link. </summary>
/// <seealso cref="OVOutlet{T}" />
public abstract class OVIntOutlet : OVOutlet<int>
{
	protected OVIntOutlet() { streamType = StreamTypes.Int; }

	/// <inheritdoc cref="OVOutlet{T}.PushSamples"/>
	protected override void PushSamples() { outlet.PushSample(samples); }
}
}
