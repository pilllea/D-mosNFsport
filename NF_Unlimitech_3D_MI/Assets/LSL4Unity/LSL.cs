//-------------------------------------------------------------------------------------------------
//
// C# API for the lab streaming layer.
//
// The lab streaming layer provides a set of functions to make instrument data accessible in real time within a lab network.
// From there, streams can be picked up by recording programs,
//
// The API covers two areas:
// - The "push API" allows to create stream outlets and to push data (regular or irregular measurement
//   time series, event data, coded audio/video frames, etc.) into them.
// - The "pull API" allows to create stream inlets and read time-synched experiment data from them
//   (for recording, viewing or experiment control).
//
//-------------------------------------------------------------------------------------------------

using System.Runtime.InteropServices;
using IntPtr = System.IntPtr;
using Exception = System.Exception;
using SerializationInfo = System.Runtime.Serialization.SerializationInfo;
using StreamingContext = System.Runtime.Serialization.StreamingContext;

namespace LSL4Unity
{
#region Enums

/// <summary> Data format of a channel (each transmitted sample holds an array of channels). </summary>
public enum ChannelFormat
{
	/// <summary> For up to 24-bit precision measurements in the appropriate physical unit. </summary>
	Float32 = 1,

	/// <summary> For universal numeric data as long as permitted by network and disk budget.
	/// (e.g., microvolts). Integers from -16777216 to 16777216 are represented accurately. </summary>
	Double64 = 2,

	/// <summary> For variable-length ASCII strings or data blobs, such as video frames,
	/// the largest representable integer is 53-bit. </summary>
	Str = 3,

	/// <summary> For high-rate digitized formats that require 32-bit precision. Depends critically on complex event descriptions, etc. </summary>
	Int32 = 4,

	/// <summary> For very high rate signals (40Khz+) or consumer-grade audio meta-data to represent meaningful units.
	/// Useful for application event codes or other coded data. </summary>
	Int16 = 5,

	/// <summary> For binary signals or other coded data. (for professional audio float is recommended). </summary>
	Int8 = 6,

	/// <summary> For now only for future compatibility. Support for this type is not yet exposed in all languages.
	/// Not recommended for encoding string data. </summary>
	Int64 = 7,

	/// <summary> Can not be transmitted. Also, some builds of liblsl will not be able to send or receive data of this type. </summary>
	Undefined = 0
}

/// <summary> Post-processing options for stream inlets. </summary>
public enum ProcessingOptions
{
	/// <summary> No automatic post-processing; return the ground-truth time stamps for manual post-processing. </summary>
	None = 0,

	/// <summary> Perform automatic clock synchronization; equivalent to manually adding the TimeCorrection() value (this is the default behavior of the inlet). </summary>
	ClockSync = 1,

	/// <summary> Remove jitter from time stamps. This will apply a smoothing algorithm to the received time stamps; to the received time stamps. </summary>
	Dejitter = 2,

	// the smoothing needs to see a minimum number of samples (30-120 seconds worst-case) until the remaining  jitter is consistently below 1ms.
	/// <summary> Force the time-stamps to be monotonically ascending (only makes sense if timestamps are dejittered). </summary>
	Monotonize = 4,

	/// <summary> Post-processing is thread-safe (same inlet can be read from by multiple threads); uses somewhat more CPU. </summary>
	Threadsafe = 8,

	/// <summary> The combination of all possible post-processing options. </summary>
	All = 1 | 2 | 4 | 8
}

#endregion

public static class LSL
{
	/// <summary> Constant to indicate that a stream has variable sampling rate. </summary>
	/// <value> Indicate that a stream has variable sampling rate. </value>
	public const double IRREGULAR_RATE = 0.0;

	/// <summary> Constant to indicate that a sample has the next successive time stamp.
	/// 
	/// This is an optional optimization to transmit less data per sample.
	/// The stamp is then deduced from the preceding one according to the stream's sampling rate
	/// (in the case of an irregular rate, the same time stamp as before will is assumed). </summary>
	/// <value> Indicate that a sample has the next successive time stamp. </value>
	public const double DEDUCED_TIMESTAMP = -1.0;

	/// <summary> A very large time duration (> 1 year) for timeout values. </summary>
	/// <remarks> Note that significantly larger numbers can cause the timeout to be invalid on some operating systems (e.g., 32-bit UNIX). </remarks>
	/// <value> A very large time duration (> 1 year). </value>
	public const double FOREVER = 32000000.0;

	/// <summary> Helper method to dispose a StreamInfo[] array </summary>
	/// <param name="arr">Array to be disposed</param>
	public static void DisposeArray(this StreamInfo[] array) { foreach (var si in array) { si.Dispose(); } }

	/// <summary> Version of the lsl protocol.
	///
	/// The major version is <see cref="ProtocolVersion"/> / 100; The minor version is <see cref="ProtocolVersion"/> % 100;
	/// Clients with different minor versions are protocol-compatible with each other while clients with different major versions will refuse to work together.</summary>
	/// <returns> The protocole version. </returns>
	public static int ProtocolVersion() { return Dll.lsl_protocol_version(); }

	/// <summary> Version of the liblsl library.
	///
	/// The major version is <see cref="LibraryVersion"/> / 100; The minor version is <see cref="LibraryVersion"/> % 100;</summary>
	/// <returns> The library version. </returns>
	public static int LibraryVersion() { return Dll.lsl_library_version(); }

	/// <summary> Obtain a local system time stamp in seconds.
	///
	/// The resolution is better than a millisecond.
	/// This reading can be used to assign time stamps to samples as they are being acquired.
	/// If the "age" of a sample is known at a particular time (e.g., from USB transmission delays),
	/// it can be used as an offset to LocalClock() to obtain a better estimate of when a sample was actually captured.
	/// See <c>StreamOutlet::PushSample()</c> for a use case. </summary>
	/// <returns> The local system time stamp in seconds. </returns>
	public static double LocalClock() { return Dll.lsl_local_clock(); }

	// ===========================
	// ==== Resolve Functions ====
	// ===========================

	/// <summary> Resolve all streams on the network.
	///
	/// This function returns all currently available streams from any outlet on the network.
	/// The network is usually the subnet specified at the local router,
	/// but may also include a multicast group of machines (given that the network supports it), or list of hostnames.
	/// These details may optionally be customized by the experimenter in a configuration file  (see Network Connectivity in the LSL wiki).
	/// This is the default mechanism used by the browsing programs and the recording program. </summary>
	/// <param name="waitTime"> The waiting time for the operation, in seconds, to search for streams. </param>
	/// <returns> An array of stream info objects (excluding their desc field), any of which can subsequently be used to open an inlet.
	/// The full info can be retrieve from the inlet. </returns>
	/// <remarks> If [waitTime] is too short (less than 0.5s) only a subset (or none) of the outlets that are present on the network may be returned.</remarks>
	public static StreamInfo[] ResolveStreams(double waitTime = 1.0)
	{
		var buf = new IntPtr[1024];
		var num = Dll.lsl_resolve_all(buf, (uint) buf.Length, waitTime);
		var res = new StreamInfo[num];
		for (var k = 0; k < num; k++) { res[k] = new StreamInfo(buf[k]); }
		return res;
	}

	/// <summary> Resolve all streams with a specific value for a given property.
	///
	/// If the goal is to resolve a specific stream, this method is preferred over resolving all streams and then selecting the desired one.</summary>
	/// <param name="prop"> The <see cref="StreamInfo"/> property that should have a specific value (e.g., "name", "type", "SourceId", or "desc/manufaturer"). </param>
	/// <param name="value"> The string value that the property should have (e.g., "EEG" as the type property). </param>
	/// <param name="minimum"> Optionally return at least this number of streams. </param>
	/// <param name="timeout"> Optionally a timeout of the operation, in seconds (default: no timeout).
	/// If the timeout expires, less than the desired number of streams (possibly none) will be returned. </param>
	/// <returns> An array of matching stream info objects (excluding their meta-data), any of which can subsequently be used to open an inlet. </returns>
	public static StreamInfo[] ResolveStream(string prop, string value, int minimum = 1, double timeout = FOREVER)
	{
		var buf = new IntPtr[1024];
		var num = Dll.lsl_resolve_byprop(buf, (uint) buf.Length, prop, value, minimum, timeout);
		var res = new StreamInfo[num];
		for (var k = 0; k < num; k++) { res[k] = new StreamInfo(buf[k]); }
		return res;
	}

	/// <summary> Resolve all streams that match a given predicate.
	///
	/// Advanced query that allows to impose more conditions on the retrieved streams;
	/// the given string is an XPath 1.0 predicate for the info node (omitting the surrounding []'s). </summary>
	/// <param name="pred"> The predicate string, e.g. "<c>name='BioSemi'</c>" or "<c>type='EEG' and starts-with(name,'BioSemi') and count(info/desc/channel)=32</c>". </param>
	/// <param name="minimum"> Optionally return at least this number of streams. </param>
	/// <param name="timeout"> Optionally a timeout of the operation, in seconds (default: no timeout).
	/// If the timeout expires, less than the desired number of streams (possibly none) will be returned. </param>
	/// <returns> An array of matching stream info objects (excluding their meta-data), any of which can subsequently be used to open an inlet. </returns>
	/// <remarks> See Also : [Wikipedia XPath 1.0](http://en.wikipedia.org/w/index.php?title=XPath_1.0). </remarks>
	public static StreamInfo[] ResolveStream(string pred, int minimum = 1, double timeout = FOREVER)
	{
		var buf = new IntPtr[1024];
		var num = Dll.lsl_resolve_bypred(buf, (uint) buf.Length, pred, minimum, timeout);
		var res = new StreamInfo[num];
		for (var k = 0; k < num; k++) { res[k] = new StreamInfo(buf[k]); }
		return res;
	}


	/// <summary> Check an error condition and throw an exception if appropriate. </summary>
	/// <param name="code"> The error code. </param>
	/// <exception cref="TimeoutException"> The operation failed due to a timeout. </exception>
	/// <exception cref="LostException"> The stream has been lost. </exception>
	/// <exception cref="ArgumentException"> An argument was incorrectly specified (e.g., wrong format or wrong length). </exception>
	/// <exception cref="InternalException"> An internal internal error has occurred. </exception>
	/// <exception cref="Exception"> An unknown error has occurred. </exception>
	public static void CheckError(int code)
	{
		if (code >= 0) { return; }
		switch (code) {
			case -1: throw new System.TimeoutException("The operation failed due to a timeout.");
			case -2: throw new LostException("The stream has been lost.");
			case -3: throw new System.ArgumentException("An argument was incorrectly specified (e.g., wrong format or wrong length).");
			case -4: throw new InternalException("An internal internal error has occurred.");
			default: throw new Exception("An unknown error has occurred.");
		}
	}
}

#region Stream

public abstract class LSLObject : SafeHandle
{
	protected IntPtr Obj => handle;


	protected LSLObject(IntPtr obj) : base(IntPtr.Zero, true)
	{
#if LSL_PRINT_OBJECT_LIFETIMES
            System.Console.Out.WriteLine($"Created object {obj:X}");
#endif
		if (obj == IntPtr.Zero) { throw new InternalException("Error creating object"); }
		SetHandle(obj);
	}

	public override bool IsInvalid => handle == IntPtr.Zero;

	/// <summary> To be implemented in inheriting classes: the liblsl function to destroy the internal object </summary>
	protected abstract void DestroyLSLObject(IntPtr obj);

	protected override bool ReleaseHandle()
	{
#if LSL_PRINT_OBJECT_LIFETIMES
            System.Console.Out.WriteLine($"Destroying object {handle:X}");
#endif
		DestroyLSLObject(handle);
		return true;
	}
}

// ==========================
// === Stream Declaration ===
// ==========================

/// <summary> The stream_info object stores the declaration of a data stream.
///
/// Represents the following information:
/// 1. stream data format (#channels, channel format)
/// 1. core information (stream name, content type, sampling rate)
/// 1. optional meta-data about the stream content (channel labels, measurement units, etc.)
///
/// Whenever a program wants to provide a new stream on the lab network,
/// it will typically first create a stream_info to describe its properties
/// and then construct a stream_outlet with it to create the stream on the network.
/// Recipients who discover the outlet can query the stream_info; it is also written to disk when recording the stream
/// (playing a similar role as a file header).</summary>
public class StreamInfo : LSLObject
{
	/// <summary> Initializes a new instance of the <see cref="StreamInfo"/> object.
	///
	/// Core stream information is specified here. Any remaining meta-data can be added later. </summary>
	/// <param name="name"> Name of the stream. Describes the device (or product series) that this stream makes available (for use by programs, experimenters or data analysts).
	/// Cannot be empty. </param>
	/// <param name="type"> Content type of the stream.
	/// Please see [https://github.com/sccn/xdf/wiki/Meta-Data](https://github.com/sccn/xdf/wiki/Meta-Data) (or web search for: meta-data)
	/// for pre-defined content-type names, but you can also make up your own.
	/// The content type is the preferred way to find streams (as opposed to searching by name). </param>
	/// <param name="channelCount"> Number of channels per sample. This stays constant for the lifetime of the stream. </param>
	/// <param name="sampling"> The sampling rate (in Hz) as advertised by the data source, if regular (otherwise set to IRREGULAR_RATE). </param>
	/// <param name="channelFormat"> Format/type of each channel.
	/// If your channels have different formats, consider supplying multiple streams or use the largest type that can hold them all (such as Double64). </param>
	/// <param name="sourceId"> Unique identifier of the device or source of the data, if available (such as the serial number).
	/// This is critical for system robustness since it allows recipients to recover from failure even after the serving app, device or computer crashes
	/// (just by finding a stream with the same source id on the network again).
	/// Therefore, it is highly recommended to always try to provide whatever information can uniquely identify the data source itself. </param>
	public StreamInfo(string        name,                                            string type, int channelCount = 1, double sampling = LSL.IRREGULAR_RATE,
					  ChannelFormat channelFormat = LSL4Unity.ChannelFormat.Float32, string sourceId = "") :
		base(Dll.lsl_create_streaminfo(name, type, channelCount, sampling, channelFormat, sourceId)) { }

	/// <summary> Initializes a new instance of the <see cref="StreamInfo"/> class. </summary>
	/// <param name="handle"> The handle. </param>
	public StreamInfo(IntPtr handle) : base(handle) { }

	protected override void DestroyLSLObject(IntPtr obj) { Dll.lsl_destroy_streaminfo(obj); }

	// ========================
	// === Core Information ===
	// ========================
	// (these fields are assigned at construction)

	/// <summary> Name of the stream.
	///
	/// This is a human-readable name. For streams offered by device modules,
	/// it refers to the type of device or product series that is generating the data of the stream.
	/// If the source is an application, the name may be a more generic or specific identifier.
	/// Multiple streams with the same name can coexist, though potentially at the cost of ambiguity (for the recording app or experimenter). </summary>
	/// <returns> The name of the stream. </returns>
	public string Name() { return Marshal.PtrToStringAnsi(Dll.lsl_get_name(Obj)); }

	/// <summary> Content type of the stream.
	///
	/// The content type is a short string such as "EEG", "Gaze" which describes the content carried by the channel (if known).
	/// If a stream contains mixed content this value need not be assigned but may instead be stored in the description of channel types.
	/// To be useful to applications and automated processing systems using the recommended content types is preferred.
	/// Content types usually follow those pre-defined in [https://github.com/sccn/xdf/wiki/Meta-Data](https://github.com/sccn/xdf/wiki/Meta-Data) (or web search for: XDF meta-data). </summary>
	/// <returns> The content type of the stream (in <c>string</c>). </returns>
	public string Type() { return Marshal.PtrToStringAnsi(Dll.lsl_get_type(Obj)); }

	/// <summary> Number of channels of the stream. A stream has at least one channel; the channel count stays constant for all samples. </summary>
	/// <returns> The Number of channels of the stream. </returns>
	public int ChannelCount() { return Dll.lsl_get_channel_count(Obj); }

	/// <summary> Sampling rate of the stream, according to the source (in Hz).
	///
	/// If a stream is irregularly sampled, this should be set to IRREGULAR_RATE.
	/// Note that no data will be lost even if this sampling rate is incorrect or if a device has temporary
	/// hiccups, since all samples will be recorded anyway (except for those dropped by the device itself). However,
	/// when the recording is imported into an application, a good importer may correct such errors more accurately
	/// if the advertised sampling rate was close to the specs of the device. </summary>
	/// <returns> The Sampling rate of the stream (in <c>double</c>). </returns>
	public double Sampling() { return Dll.lsl_get_nominal_srate(Obj); }

	/// <summary> Channel format of the stream.
	///
	/// All channels in a stream have the same format. However, a device might offer multiple time-synched streams each with its own format. </summary>
	/// <returns>The hannel format of the stream (in <see cref="LSL4Unity.ChannelFormat"/> enum) </returns>
	public ChannelFormat ChannelFormat() { return Dll.lsl_get_channel_format(Obj); }

	/// <summary> Unique identifier of the stream's source, if available.
	///
	/// The unique source (or device) identifier is an optional piece of information that, if available,
	/// allows that endpoints(such as the recording program) can re-acquire a stream automatically once it is back online. </summary>
	/// <returns> The Identifier (in <c>string</c>). </returns>
	public string SourceId() { return Marshal.PtrToStringAnsi(Dll.lsl_get_source_id(Obj)); }


	// ======================================
	// === Additional Hosting Information ===
	// ======================================
	// (these fields are implicitly assigned once bound to an outlet/inlet)

	/// <summary> Protocol version used to deliver the stream. </summary>
	/// <returns> The protocol version (in <c>int</c>). </returns>
	public int Version() { return Dll.lsl_get_version(Obj); }

	/// <summary> Creation time stamp of the stream.
	///
	/// This is the time stamp when the stream was first created (as determined via LocalClock() on the providing machine). </summary>
	/// <returns> The Time Stamp (in <c>double</c>). </returns>
	public double CreatedAt() { return Dll.lsl_get_created_at(Obj); }

	/// <summary> Unique ID of the stream outlet instance (once assigned).
	///
	/// This is a unique identifier of the stream outlet, and is guaranteed to be different across multiple instantiations of the same outlet (e.g., after a re-start). </summary>
	/// <returns> The Unique Identifier (in <c>string</c>). </returns>
	public string Uid() { return Marshal.PtrToStringAnsi(Dll.lsl_get_uid(Obj)); }

	/// <summary> Session ID for the given stream.
	///
	/// The session id is an optional human-assigned identifier of the recording session.
	/// While it is rarely used, it can be used to prevent concurrent recording activitites on the same sub-network (e.g., in multiple experiment areas)
	/// from seeing each other's streams (assigned via a configuration file by the experimenter, see Network Connectivity in the LSL wiki). </summary>
	/// <returns> The Session Identifier (in <c>string</c>). </returns>
	public string SessionId() { return Marshal.PtrToStringAnsi(Dll.lsl_get_session_id(Obj)); }

	/// <summary> Hostname of the providing machine. </summary>
	/// <returns> The Hostname (in <c>string</c>). </returns>
	public string Hostname() { return Marshal.PtrToStringAnsi(Dll.lsl_get_hostname(Obj)); }

	// ========================
	// === Data Description ===
	// ========================

	/// <summary> Extended description of the stream.
	///
	/// It is highly recommended that at least the channel labels are described here.
	/// See code examples on the LSL wiki. Other information, such as amplifier settings,
	/// measurement units if deviating from defaults, setup information, subject information, etc.,
	/// can be specified here, as well. Meta-data recommendations follow the XDF file format project
	/// ([https://github.com/sccn/xdf/wiki/Meta-Data](https://github.com/sccn/xdf/wiki/Meta-Data) or web search for: XDF meta-data). </summary>
	/// <remarks>if you use a stream content type for which meta-data recommendations exist, please
	/// try to lay out your meta-data in agreement with these recommendations for compatibility with other applications. </remarks>
	/// <returns> A <see cref="XMLElement "/> containing the description. </returns>
	public XMLElement Desc() { return new XMLElement(Dll.lsl_get_desc(Obj)); }

	/// <summary> Retrieve the entire <see cref="StreamInfo"/> in XML format.
	///
	/// This yields an XML document (in string form) whose top-level element is &lt;info&gt;. The info element contains
	/// one element for each field of the stream_info class, including:
	///  a) the core elements <c>Name</c>, <c>Type</c>, <c>ChannelCount</c>, <c>Sampling</c>, <c>ChannelFormat</c>, <c>SourceId</c>
	///  b) the misc elements <c>Version</c>, <c>CreatedAt</c>, <c>Uid</c>, <c>SessionId</c>, <c>v4address</c>, <c>v4data_port</c>, <c>v4service_port</c>, <c>v6address</c>, <c>v6data_port</c>, <c>v6service_port</c>
	///  c) the extended description element <c>desc</c> with user-defined sub-elements. </summary>
	/// <returns> A <c>string</c> with the entire <see cref="StreamInfo"/>. </returns>
	public string AsXML()
	{
		var pXml = Dll.lsl_get_xml(Obj);
		var res  = Marshal.PtrToStringAnsi(pXml);
		Dll.lsl_destroy_string(pXml);
		return res;
	}
}


// =======================
// ==== Stream Outlet ====
// =======================

/// <summary> A stream outlet.
///
/// Outlets are used to make streaming data (and the meta-data) available on the lab network. </summary>
public class StreamOutlet : LSLObject
{
	/// <summary> Initializes a new instance of <see cref="StreamOutlet"/>. This makes the stream discoverable. </summary>
	/// <param name="info"> The stream information to use for creating this stream. Stays constant over the lifetime of the outlet. </param>
	/// <param name="chunkSize"> Optionally the desired chunk granularity (in samples) for transmission.
	/// If unspecified, each push operation yields one chunk. Inlets can override this setting. </param>
	/// <param name="maxBuffered">Optionally the maximum amount of data to buffer (in seconds if there is a nominal sampling rate, otherwise x100 in samples).
	/// The default is 6 minutes of data. </param>
	public StreamOutlet(StreamInfo info, int chunkSize = 0, int maxBuffered = 360) : 
		base(Dll.lsl_create_outlet(info.DangerousGetHandle(), chunkSize, maxBuffered)) { }

	protected override void DestroyLSLObject(IntPtr obj) { Dll.lsl_destroy_outlet(obj); }

	// ========================================
	// === Pushing a sample into the outlet ===
	// ========================================

	/// <summary> Push an array of values as a sample into the outlet. </summary>
	/// <param name="data"> An array of values to push (one for each channel). </param>
	/// <param name="time"> Optionally the capture time of the sample, in agreement with <see cref="LocalClock"/>; if omitted, the current time is used. </param>
	/// <param name="pushthrough"> Optionally whether to push the sample through to the receivers instead of buffering it with subsequent samples.
	/// Note that the chunk_size, if specified at outlet construction, takes precedence over the pushthrough flag. </param>
	public void PushSample(float[] data, double time = 0.0, bool pushthrough = true) { Dll.lsl_push_sample_ftp(Obj, data, time, pushthrough ? 1 : 0); }

	/// <inheritdoc cref="PushSample(float[],double,bool)"/>
	/// <param name="data"> An array of values to push (one for each channel). </param>
	public void PushSample(double[] data, double time = 0.0, bool pushthrough = true) { Dll.lsl_push_sample_dtp(Obj, data, time, pushthrough ? 1 : 0); }

	/// <inheritdoc cref="PushSample(float[],double,bool)"/>
	/// <param name="data"> An array of values to push (one for each channel). </param>
	public void PushSample(int[] data, double time = 0.0, bool pushthrough = true) { Dll.lsl_push_sample_itp(Obj, data, time, pushthrough ? 1 : 0); }

	/// <inheritdoc cref="PushSample(float[],double,bool)"/>
	/// <param name="data"> An array of values to push (one for each channel). </param>
	public void PushSample(short[] data, double time = 0.0, bool pushthrough = true) { Dll.lsl_push_sample_stp(Obj, data, time, pushthrough ? 1 : 0); }

	/// <inheritdoc cref="PushSample(float[],double,bool)"/>
	/// <param name="data"> An array of values to push (one for each channel). </param>
	public void PushSample(char[] data, double time = 0.0, bool pushthrough = true) { Dll.lsl_push_sample_ctp(Obj, data, time, pushthrough ? 1 : 0); }

	/// <inheritdoc cref="PushSample(float[],double,bool)"/>
	/// <param name="data"> An array of values to push (one for each channel). </param>
	public void PushSample(string[] data, double time = 0.0, bool pushthrough = true) { Dll.lsl_push_sample_strtp(Obj, data, time, pushthrough ? 1 : 0); }

	// ===================================================
	// === Pushing an chunk of samples into the outlet ===
	// ===================================================

	/// <summary> Push a chunk of samples into the outlet. Single time provided. </summary>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	/// <param name="time"> Optionally the capture time of the sample, in agreement with <see cref="LocalClock"/>; if omitted, the current time is used. </param>
	/// <param name="pushthrough"> Optionally whether to push the sample through to the receivers instead of buffering it with subsequent samples.
	/// Note that the chunkSize, if specified at outlet construction, takes precedence over the pushthrough flag. </param>
	public void PushChunk(float[,] data, double time = 0.0, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_ftp(Obj, data, (uint) data.Length, time, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double,bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(double[,] data, double time = 0.0, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_dtp(Obj, data, (uint) data.Length, time, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double,bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(int[,] data, double time = 0.0, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_itp(Obj, data, (uint) data.Length, time, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double,bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(short[,] data, double time = 0.0, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_stp(Obj, data, (uint) data.Length, time, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double,bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(char[,] data, double time = 0.0, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_ctp(Obj, data, (uint) data.Length, time, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double,bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(string[,] data, double time = 0.0, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_strtp(Obj, data, (uint) data.Length, time, pushthrough ? 1 : 0);
	}


	/// <summary> Push a chunk of multiplexed samples into the outlet. One time per sample is provided. </summary>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(float[,] data, double[] times, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_ftnp(Obj, data, (uint) data.Length, times, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double[],bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(double[,] data, double[] times, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_dtnp(Obj, data, (uint) data.Length, times, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double[],bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(int[,] data, double[] times, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_itnp(Obj, data, (uint) data.Length, times, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double[],bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(short[,] data, double[] times, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_stnp(Obj, data, (uint) data.Length, times, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double[],bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(char[,] data, double[] times, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_ctnp(Obj, data, (uint) data.Length, times, pushthrough ? 1 : 0);
	}

	/// <inheritdoc cref="PushChunk(float[,],double[],bool)"/>
	/// <param name="data"> A rectangular array of values for multiple samples. </param>
	public void PushChunk(string[,] data, double[] times, bool pushthrough = true)
	{
		Dll.lsl_push_chunk_strtnp(Obj, data, (uint) data.Length, times, pushthrough ? 1 : 0);
	}


	// ===============================
	// === Miscellaneous Functions ===
	// ===============================

	/// <summary> Check whether consumers are currently registered.
	///
	///  While it does not hurt, there is technically no reason to push samples if there is no consumer. </summary>
	/// <returns> <c>true</c> or <c>false</c>. </returns>
	public bool HaveConsumers() { return Dll.lsl_have_consumers(Obj) > 0; }

	/// <summary> Wait until some consumer shows up (without wasting resources). </summary>
	/// <param name="timeout"> The timeout. </param>
	/// <returns> True if the wait was successful, false if the timeout expired. </returns>
	public bool WaitForConsumers(double timeout = LSL.FOREVER) { return Dll.lsl_wait_for_consumers(Obj, timeout) > 0; }

	/// <summary> Retrieve the stream info provided by this outlet.
	///
	/// This is what was used to create the stream (and also has the Additional Network Information fields assigned). </summary>
	/// <returns> A <see cref="StreamInfo"/> </returns>
	public StreamInfo Info() { return new StreamInfo(Dll.lsl_get_info(Obj)); }
}


// ======================
// ==== Stream Inlet ====
// ======================

/// <summary> A stream inlet.
///
/// Inlets are used to receive streaming data (and meta-data) from the lab network. </summary>
public class StreamInlet : LSLObject
{
	/// <summary> Initializes a new instance of <see cref="StreamInlet"/> from a resolved stream info. </summary>
	/// <param name="info"> A resolved stream info object (as coming from one of the resolver functions). </param>
	/// <param name="maxBuflen"> Optionally the maximum amount of data to buffer
	/// (in seconds if there is a nominal sampling rate, otherwise x100 in samples).
	/// Recording applications want to use a fairly large buffer size here,
	/// while real-time applications would only buffer as much as they need to perform their next calculation. </param>
	/// <param name="maxChunklen"> Optionally the maximum size, in samples, at which chunks are transmitted
	/// (the default corresponds to the chunk sizes used by the sender).
	/// Recording applications can use a generous size here (leaving it to the network how to pack things),
	/// while real-time applications may want a finer (perhaps 1-sample) granularity.
	/// If left unspecified (=0), the sender determines the chunk granularity. </param>
	/// <param name="recover"> Try to silently recover lost streams that are recoverable (=those that that have a SourceId set).
	/// In all other cases (recover is false or the stream is not recoverable) functions may throw a <see cref="LostException"/>
	/// if the stream's source is lost (e.g., due to an app or computer crash). </param>
	/// <remarks> The <see cref="StreamInlet"/> may also be constructed with a fully-specified <see cref="StreamInfo"/>
	/// if the desired channel format and count is already known up-front,
	/// but this is strongly discouraged and should only ever be done if there is no time to resolve the stream up-front
	/// (e.g., due to limitations in the client program). </remarks>
	public StreamInlet(StreamInfo        info, int maxBuflen = 360, int maxChunklen = 0, bool recover = true,
					   ProcessingOptions postproc_flags = ProcessingOptions.None) : 
		base(Dll.lsl_create_inlet(info.DangerousGetHandle(), maxBuflen, maxChunklen, recover ? 1 : 0))
	{ 
		Dll.lsl_set_postprocessing(Obj, postproc_flags);
	}

	protected override void DestroyLSLObject(IntPtr obj) { Dll.lsl_destroy_inlet(obj); }

	/// <summary> Retrieve the complete information of the given stream, including the extended description.
	///
	/// Can be invoked at any time of the stream's lifetime.  </summary>
	/// <param name="timeout"> Optional timeout of the operation (default: no timeout). </param>
	/// <returns> <see cref="StreamInfo"/>. </returns>
	/// <exception cref="TimeoutException"> If the timeout expires. </exception>
	/// <exception cref="LostException"> If the stream source has been lost. </exception>
	public StreamInfo Info(double timeout = LSL.FOREVER)
	{
		var ec  = 0;
		var res = Dll.lsl_get_fullinfo(Obj, timeout, ref ec);
		LSL.CheckError(ec);
		return new StreamInfo(res);
	}

	/// <summary> Subscribe to the data stream.
	///
	/// All samples pushed in at the other end from this moment onwards will be queued and
	/// eventually be delivered in response to <see cref="PullSample(float[],double)"/> or <see cref="PullChunk(float[,],double[],double)"/> calls.
	/// Pulling a sample without some preceding OpenStream is permitted (the stream will then be opened implicitly). </summary>
	/// <param name="timeout"> Optional timeout of the operation (default: no timeout). </param>
	/// <exception cref="TimeoutException"> If the timeout expires. </exception>
	/// <exception cref="LostException"> If the stream source has been lost. </exception>
	public void OpenStream(double timeout = LSL.FOREVER)
	{
		var ec = 0;
		Dll.lsl_open_stream(Obj, timeout, ref ec);
		LSL.CheckError(ec);
	}

	/// <summary> Drop the current data stream.
	/// 
	/// All samples that are still buffered or in flight will be dropped and transmission
	/// and buffering of data for this inlet will be stopped. If an application stops being
	/// interested in data from a source (temporarily or not) but keeps the outlet alive,
	/// it should call CloseStream() to not waste unnecessary system and network resources. </summary>
	public void CloseStream() { Dll.lsl_close_stream(Obj); }

	/// <summary> Retrieve an estimated time correction offset for the given stream.
	///
	/// The first call to this function takes several miliseconds until a reliable first estimate is obtained.
	/// Subsequent calls are instantaneous (and rely on periodic background updates).
	/// The precision of these estimates should be below 1 ms (empirically within +/-0.2 ms). </summary>
	/// <param name="timeout"> Optional timeout to acquire the first time-correction estimate (default: no timeout). </param>
	/// <returns> The time correction estimate. This is the number that needs to be added to a time stamp
	/// that was remotely generated via <c>lsl_local_clock()</c> to map it into the local clock domain of this machine. </returns>
	/// <exception cref="TimeoutException"> If the timeout expires. </exception>
	/// <exception cref="LostException"> If the stream source has been lost. </exception>
	public double TimeCorrection(double timeout = LSL.FOREVER)
	{
		var ec  = 0;
		var res = Dll.lsl_time_correction(Obj, timeout, ref ec);
		LSL.CheckError(ec);
		return res;
	}

	// =======================================
	// === Pulling a sample from the inlet ===
	// =======================================

	/// <summary> Pull a sample from the inlet and read it into an array of values. Handles type checking and conversion. </summary>
	/// <param name="sample"> An array to hold the resulting values. </param>
	/// <param name="timeout"> Optional, the timeout for this operation, if any. Use 0.0 to make the function non-blocking. </param>
	/// <returns>The capture time of the sample on the remote machine, or 0.0 if no new sample was available.
	/// To remap this time stamp to the local clock, add the value returned by <see cref="TimeCorrection"/> to it. </returns>
	/// <exception cref="LostException"> If the stream source has been lost. </exception>
	public double PullSample(float[] sample, double timeout = LSL.FOREVER)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_sample_f(Obj, sample, sample.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return res;
	}

	/// <inheritdoc cref="PullSample(float[],double)"/>
	/// <param name="sample"> An array to hold the resulting values. </param>
	public double PullSample(double[] sample, double timeout = LSL.FOREVER)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_sample_d(Obj, sample, sample.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return res;
	}

	/// <inheritdoc cref="PullSample(float[],double)"/>
	/// <param name="sample"> An array to hold the resulting values. </param>
	public double PullSample(int[] sample, double timeout = LSL.FOREVER)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_sample_i(Obj, sample, sample.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return res;
	}

	/// <inheritdoc cref="PullSample(float[],double)"/>
	/// <param name="sample"> An array to hold the resulting values. </param>
	public double PullSample(short[] sample, double timeout = LSL.FOREVER)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_sample_s(Obj, sample, sample.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return res;
	}

	/// <inheritdoc cref="PullSample(float[],double)"/>
	/// <param name="sample"> An array to hold the resulting values. </param>
	public double PullSample(char[] sample, double timeout = LSL.FOREVER)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_sample_c(Obj, sample, sample.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return res;
	}

	/// <inheritdoc cref="PullSample(float[],double)"/>
	/// <param name="sample"> An array to hold the resulting values. </param>
	public double PullSample(string[] sample, double timeout = LSL.FOREVER)
	{
		var ec  = 0;
		var tmp = new IntPtr[sample.Length];
		var res = Dll.lsl_pull_sample_str(Obj, tmp, tmp.Length, timeout, ref ec);
		LSL.CheckError(ec);
		try {
			for (var k = 0; k < tmp.Length; k++) { sample[k] = Marshal.PtrToStringAnsi(tmp[k]); }
		}
		finally {
			foreach (var t in tmp) { Dll.lsl_destroy_string(t); }
		}
		return res;
	}


	// =================================================
	// === Pulling a chunk of samples from the inlet ===
	// =================================================

	/// <summary> Pull a chunk of data from the inlet. </summary>
	/// <param name="buffer"> A pre-allocated buffer where the channel data shall be stored. </param>
	/// <param name="times"> A pre-allocated buffer where time stamps shall be stored. </param>
	/// <param name="timeout"> Optionally the timeout for this operation, if any.
	/// When the timeout expires, the function may return before the entire buffer is filled.
	/// The default value of 0.0 will retrieve only data available for immediate pickup. </param>
	/// <returns> Number of samples written to the data and timestamp buffers.</returns>
	/// <exception cref="LostException"> If the stream source has been lost. </exception>
	public int PullChunk(float[,] buffer, double[] times, double timeout = 0.0)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_chunk_f(Obj, buffer, times, (uint) buffer.Length, (uint) times.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return (int) res / buffer.GetLength(1);
	}

	/// <inheritdoc cref="PullChunk(float[,],double[],double)"/>
	/// <param name="buffer"> A pre-allocated buffer where the channel data shall be stored. </param>
	public int PullChunk(double[,] buffer, double[] times, double timeout = 0.0)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_chunk_d(Obj, buffer, times, (uint) buffer.Length, (uint) times.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return (int) res / buffer.GetLength(1);
	}

	/// <inheritdoc cref="PullChunk(float[,],double[],double)"/>
	/// <param name="buffer"> A pre-allocated buffer where the channel data shall be stored. </param>
	public int PullChunk(int[,] buffer, double[] times, double timeout = 0.0)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_chunk_i(Obj, buffer, times, (uint) buffer.Length, (uint) times.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return (int) res / buffer.GetLength(1);
	}

	/// <inheritdoc cref="PullChunk(float[,],double[],double)"/>
	/// <param name="buffer"> A pre-allocated buffer where the channel data shall be stored. </param>
	public int PullChunk(short[,] buffer, double[] times, double timeout = 0.0)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_chunk_s(Obj, buffer, times, (uint) buffer.Length, (uint) times.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return (int) res / buffer.GetLength(1);
	}

	/// <inheritdoc cref="PullChunk(float[,],double[],double)"/>
	/// <param name="buffer"> A pre-allocated buffer where the channel data shall be stored. </param>
	public int PullChunk(char[,] buffer, double[] times, double timeout = 0.0)
	{
		var ec  = 0;
		var res = Dll.lsl_pull_chunk_c(Obj, buffer, times, (uint) buffer.Length, (uint) times.Length, timeout, ref ec);
		LSL.CheckError(ec);
		return (int) res / buffer.GetLength(1);
	}

	/// <inheritdoc cref="PullChunk(float[,],double[],double)"/>
	/// <param name="buffer"> A pre-allocated buffer where the channel data shall be stored. </param>
	public int PullChunk(string[,] buffer, double[] times, double timeout = 0.0)
	{
		var ec  = 0;
		var tmp = new IntPtr[buffer.GetLength(0), buffer.GetLength(1)];
		var res = Dll.lsl_pull_chunk_str(Obj, tmp, times, (uint) tmp.Length, (uint) times.Length, timeout, ref ec);
		LSL.CheckError(ec);
		try {
			for (var s = 0; s < tmp.GetLength(0); s++) {
				for (var c = 0; c < tmp.GetLength(1); c++) { buffer[s, c] = Marshal.PtrToStringAnsi(tmp[s, c]); }
			}
		}
		finally {
			for (var s = 0; s < tmp.GetLength(0); s++) {
				for (var c = 0; c < tmp.GetLength(1); c++) { Dll.lsl_destroy_string(tmp[s, c]); }
			}
		}
		return (int) res / buffer.GetLength(1);
	}

	/// <summary>  Query whether samples are currently available for immediate pickup. </summary>
	/// <returns> Number of samples available. </returns>
	/// <remarks> that it is not a good idea to use <see cref="SamplesAvailable"/> to determine whether a <c>pull_*()</c> call would block:
	/// to be sure, set the pull timeout to 0.0 or an acceptably low value.
	/// If the underlying implementation supports it, the value will be the number of samples available (otherwise it will be 1 or 0). </remarks>
	public int SamplesAvailable() { return (int) Dll.lsl_samples_available(Obj); }

	/// <summary> Query whether the clock was potentially reset since the last call to <see cref="WasClockReset"/>.
	///
	/// This is a rarely-used function that is only useful to applications that combine multiple <see cref="TimeCorrection"/> values to estimate precise clock drift;
	/// it allows to tolerate cases where the source machine was hot-swapped or restarted in between two measurements. </summary>
	/// <returns> <c>true</c> if clock was reset... </returns>
	public bool WasClockReset() { return (int) Dll.lsl_was_clock_reset(Obj) != 0; }
}

// ===========================
// === Continuous Resolver ===
// ===========================


/// <summary> A convenience class that resolves streams continuously in the background throughout
/// its lifetime and which can be queried at any time for the set of streams that are currently  visible on the network. </summary>
public class ContinuousResolver : LSLObject
{
	/// <summary> Initializes a new instance of the <see cref="ContinuousResolver"/> class that resolves all streams on the network.
	/// 
	/// This is analogous to the functionality offered by the free function <see cref="ResolveStreams"/>.</summary>
	/// <param name="forgetAfter"> When a stream is no longer visible on the network (e.g., because it was shut down),
	/// this is the time in seconds after which it is no longer reported by the resolver. </param>
	public ContinuousResolver(double forgetAfter = 5.0) : base(Dll.lsl_create_continuous_resolver(forgetAfter)) { }

	/// <summary> Initializes a new instance of the <see cref="ContinuousResolver"/> class that resolves all streams with a specific value for a given property.
	/// 
	/// This is analogous to the functionality provided by the free function <see cref="ResolveStream(string,string,int,double)"/>.</summary>
	/// <param name="prop"> The <see cref="StreamInfo"/> property that should have a specific value (e.g., "name", "type", "SourceId", or "desc/manufaturer"). </param>
	/// <param name="value"> The string value that the property should have (e.g., "EEG" as the type property). </param>
	/// <param name="forgetAfter"> When a stream is no longer visible on the network (e.g., because it was shut down),
	/// this is the time in seconds after which it is no longer reported by the resolver. </param>
	public ContinuousResolver(string prop, string value, double forgetAfter = 5.0) :
		base(Dll.lsl_create_continuous_resolver_byprop(prop, value, forgetAfter)) { }

	/// <summary> Initializes a new instance of the <see cref="ContinuousResolver"/> class that resolves all streams that match a given XPath 1.0 predicate.
	/// 
	/// This is analogous to the functionality provided by the free function <see cref="ResolveStream(string,int,double)"/>. </summary>
	/// <param name="pred"> The predicate string, e.g. "name='BioSemi'" or "type='EEG' and starts-with(name,'BioSemi') and count(info/desc/channel)=32" </param>
	/// <param name="forgetAfter"> When a stream is no longer visible on the network (e.g., because it was shut down),
	/// this is the time in seconds after which it is no longer reported by the resolver. </param>
	public ContinuousResolver(string pred, double forgetAfter = 5.0) : base(Dll.lsl_create_continuous_resolver_bypred(pred, forgetAfter)) { }


	protected override void DestroyLSLObject(IntPtr obj) { Dll.lsl_destroy_continuous_resolver(obj); }

	/// <summary> Obtain the set of currently present streams on the network (i.e. resolve result). </summary>
	/// <returns> An array of matching stream info objects (excluding their meta-data), any of which can subsequently be used to open an inlet. </returns>
	public StreamInfo[] Results()
	{
		var buf = new IntPtr[1024];
		var num = Dll.lsl_resolver_results(Obj, buf, (uint) buf.Length);
		var res = new StreamInfo[num];
		for (var k = 0; k < num; k++) { res[k] = new StreamInfo(buf[k]); }
		return res;
	}
}

#endregion

// =====================
// ==== XML Element ====
// =====================

/// <summary> A lightweight XML element tree; models the <see cref="StreamInfo.Desc"/> field of <see cref="StreamInfo"/>.
///
/// Has a name and can have multiple named children or have text content as value; attributes are omitted.
/// Insider note: The interface is modeled after a subset of pugixml's node type and is compatible with it. </summary>
/// <remarks> See Also : [http://pugixml.googlecode.com/svn/tags/latest/docs/manual/access.html](http://pugixml.googlecode.com/svn/tags/latest/docs/manual/access.html). </remarks>
public struct XMLElement
{
	/// <summary> Initializes a new instance of <see cref="XMLElement"/> struct. </summary>
	/// <param name="handle"> The handle. </param>
	public XMLElement(IntPtr handle) { _obj = handle; }

	// === Tree Navigation ===

	/// <summary> Get the first child of the element. </summary>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement FirstChild() { return new XMLElement(Dll.lsl_first_child(_obj)); }

	/// <summary> Get the last child of the element. </summary>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement LastChild() { return new XMLElement(Dll.lsl_last_child(_obj)); }

	/// <summary> Get the next sibling in the children list of the parent node. </summary>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement NextSibling() { return new XMLElement(Dll.lsl_next_sibling(_obj)); }

	/// <summary> Get the previous sibling in the children list of the parent node. </summary>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement PreviousSibling() { return new XMLElement(Dll.lsl_previous_sibling(_obj)); }

	/// <summary> Get the parent node. </summary>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement Parent() { return new XMLElement(Dll.lsl_parent(_obj)); }


	// === Tree Navigation by Name ===

	/// <summary> Get a child with a specified name. </summary>
	/// <param name="name"> The child name. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement Child(string name) { return new XMLElement(Dll.lsl_child(_obj, name)); }

	/// <summary> Get the next sibling with the specified name. </summary>
	/// <param name="name"> The next sibling name. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement NextSibling(string name) { return new XMLElement(Dll.lsl_next_sibling_n(_obj, name)); }

	/// <summary> Get the previous sibling with the specified name. </summary>
	/// <param name="name"> The previous sibling name. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement PreviousSibling(string name) { return new XMLElement(Dll.lsl_previous_sibling_n(_obj, name)); }


	// === Content Queries ===

	/// <summary> Whether this node is empty. </summary>
	/// <returns> <c>true</c> or <c>false</c>. </returns>
	public bool Empty() { return Dll.lsl_empty(_obj) != 0; }

	/// <summary> Whether this is a text body (instead of an XML element). True both for plain char data and CData. </summary>
	/// <returns> <c>true</c> or <c>false</c>. </returns>
	public bool IsText() { return Dll.lsl_is_text(_obj) != 0; }

	/// <summary> Name of the element. </summary>
	/// <returns> the name as <c>string</c>. </returns>
	public string Name() { return Marshal.PtrToStringAnsi(Dll.lsl_name(_obj)); }

	/// <summary> Value of the element. </summary>
	/// <returns> the value as <c>string</c>. </returns>
	public string Value() { return Marshal.PtrToStringAnsi(Dll.lsl_value(_obj)); }

	/// <summary> Get child value (value of the first child that is text). </summary>
	/// <returns> the child value as <c>string</c>. </returns>
	public string ChildValue() { return Marshal.PtrToStringAnsi(Dll.lsl_child_value(_obj)); }

	/// <summary> Get child value of a child with a specified name. </summary>
	/// <param name="name"> The child name. </param>
	/// <returns> the child value as <c>string</c>. </returns>
	public string ChildValue(string name) { return Marshal.PtrToStringAnsi(Dll.lsl_child_value_n(_obj, name)); }


	// === Modification ===

	/// <summary> Append a child node with a given name, which has a (nameless) plain-text child with the given text value. </summary>
	/// <param name="name"> The child name. </param>
	/// <param name="value"> The child value. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement AppendChildValue(string name, string value) { return new XMLElement(Dll.lsl_append_child_value(_obj, name, value)); }

	/// <summary> Prepend a child node with a given name, which has a (nameless) plain-text child with the given text value. </summary>
	/// <param name="name"> The child name. </param>
	/// <param name="value"> The child value. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement PrependChildValue(string name, string value) { return new XMLElement(Dll.lsl_prepend_child_value(_obj, name, value)); }

	/// <summary> Set the text value of the (nameless) plain-text child of a named child node. </summary>
	/// <param name="name"> The child name. </param>
	/// <param name="value"> The child value. </param>
	/// <returns> True if the wait was successful, false otherwise. </returns>
	public bool SetChildValue(string name, string value) { return Dll.lsl_set_child_value(_obj, name, value) != 0; }

	/// <summary> Set the element's name. </summary>
	/// <param name="name"> The new name. </param>
	/// <returns> <c>false</c> if the node is empty. </returns>
	public bool SetName(string name) { return Dll.lsl_set_name(_obj, name) != 0; }

	/// <summary> Set the element's value. </summary>
	/// <param name="value"> The new value. </param>
	/// <returns> <c>false</c> if the node is empty. </returns>
	public bool SetValue(string value) { return Dll.lsl_set_value(_obj, value) != 0; }

	/// <summary> Append a child element with the specified name. </summary>
	/// <param name="name"> The child name. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement AppendChild(string name) { return new XMLElement(Dll.lsl_append_child(_obj, name)); }

	/// <summary> Prepend a child element with the specified name. </summary>
	/// <param name="name"> The child name. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement PrependChild(string name) { return new XMLElement(Dll.lsl_prepend_child(_obj, name)); }

	/// <summary> Append a copy of the specified element as a child. </summary>
	/// <param name="e"> The element to copy. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement AppendCopy(XMLElement e) { return new XMLElement(Dll.lsl_append_copy(_obj, e._obj)); }

	/// <summary> Prepend a child element with the specified name. </summary>
	/// <param name="e"> The element to copy. </param>
	/// <returns> New <see cref="XMLElement"/>. </returns>
	public XMLElement PrependCopy(XMLElement e) { return new XMLElement(Dll.lsl_prepend_copy(_obj, e._obj)); }

	/// <summary> Remove a child element with the specified name. </summary>
	/// <param name="name"> The child name. </param>
	public void RemoveChild(string name) { Dll.lsl_remove_child_n(_obj, name); }

	/// <summary> Remove a specified child element. </summary>
	/// <param name="e"> The element to remove. </param>
	public void RemoveChild(XMLElement e) { Dll.lsl_remove_child(_obj, e._obj); }

	private readonly IntPtr _obj;
}

// =======================
// === Exception Types ===
// =======================

#region Exception Types

/// <summary> Exception class that indicates that a stream inlet's source has been irrecoverably lost. </summary>
/// <seealso cref="System.Exception" />
public class LostException : Exception
{
	public LostException(string               message = "", Exception        inner = null) { }
	protected LostException(SerializationInfo info,         StreamingContext context) { }
}

/// <summary> Exception class that indicates that an internal error has occurred inside liblsl. </summary>
/// <seealso cref="System.Exception" />
public class InternalException : Exception
{
	public InternalException(string               message = "", Exception        inner = null) { }
	protected InternalException(SerializationInfo info,         StreamingContext context) { }
}

#endregion

#region Internal: C library function definitions

internal static class Dll
{
#if (UNITY_EDITOR_WIN && UNITY_EDITOR_64)
	private const string LIBNAME = "liblsl64";
#elif UNITY_EDITOR_WIN
	private const string LIBNAME = "liblsl32";
#elif UNITY_STANDALONE_WIN
	// a build hook will took care that the correct dll will be renamed after a successfull build
	private const string LIBNAME = "liblsl";
#elif (UNITY_EDITOR_LINUX && UNITY_EDITOR_64) || UNITY_STANDALONE_LINUX
	private const string LIBNAME = "liblsl64.so";
#elif UNITY_EDITOR_LINUX
	private const string LIBNAME = "liblsl32.so";
#elif UNITY_STANDALONE_LINUX
	private const string LIBNAME = "liblsl.so";
#elif Unity_EDITOR_OSX || UNITY_STANDALONE_OSX
	//32-bit dylib no longer provided.
	private const string LIBNAME = "liblsl64";
#elif UNITY_STANDALONE_OSX
	private const string LIBNAME = "liblsl";
#elif UNITY_ANDROID
	private const string LIBNAME = "lslAndroid";
#endif

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_protocol_version();

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_library_version();

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_local_clock();

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_create_streaminfo(string name, string type, int channelCount, double sampling, ChannelFormat channelFormat,
													  string sourceId);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_destroy_streaminfo(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_name(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_type(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_get_channel_count(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_get_nominal_srate(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern ChannelFormat lsl_get_channel_format(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_source_id(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_get_version(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_get_created_at(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_uid(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_session_id(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_hostname(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_desc(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_xml(IntPtr info);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_create_outlet(IntPtr info, int chunkSize, int maxBuffered);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_destroy_outlet(IntPtr obj);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_sample_ftp(IntPtr obj, float[] data, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_sample_dtp(IntPtr obj, double[] data, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_sample_itp(IntPtr obj, int[] data, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_sample_stp(IntPtr obj, short[] data, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_sample_ctp(IntPtr obj, char[] data, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_sample_strtp(IntPtr obj, string[] data, double timestamp, int pushthrough);

	//[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	//public static extern int lsl_push_sample_buftp(IntPtr obj, char[][] data, uint[] lengths, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_ftp(IntPtr obj, float[,] data, uint dataElements, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_ftnp(IntPtr obj, float[,] data, uint dataElements, double[] timestamps, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_dtp(IntPtr obj, double[,] data, uint dataElements, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_dtnp(IntPtr obj, double[,] data, uint dataElements, double[] timestamps, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_itp(IntPtr obj, int[,] data, uint dataElements, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_itnp(IntPtr obj, int[,] data, uint dataElements, double[] timestamps, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_stp(IntPtr obj, short[,] data, uint dataElements, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_stnp(IntPtr obj, short[,] data, uint dataElements, double[] timestamps, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_ctp(IntPtr obj, char[,] data, uint dataElements, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_ctnp(IntPtr obj, char[,] data, uint dataElements, double[] timestamps, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_strtp(IntPtr obj, string[,] data, uint dataElements, double timestamp, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_push_chunk_strtnp(IntPtr obj, string[,] data, uint dataElements, double[] timestamps, int pushthrough);

	//[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	//public static extern int lsl_push_chunk_buftp(IntPtr obj, char[][] data, uint[] lengths, uint dataElements, double timestamp, int pushthrough);

	//[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	//public static extern int lsl_push_chunk_buftnp(IntPtr obj, char[][] data, uint[] lengths, uint dataElements, double[] timestamps, int pushthrough);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_have_consumers(IntPtr obj);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_wait_for_consumers(IntPtr obj, double timeout);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_info(IntPtr obj);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_resolve_all(IntPtr[] buffer, uint bufferElements, double waitTime);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_resolve_byprop(IntPtr[] buffer, uint bufferElements, string prop, string value, int minimum, double waitTime);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_resolve_bypred(IntPtr[] buffer, uint bufferElements, string pred, int minimum, double waitTime);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_create_inlet(IntPtr info, int maxBuflen, int maxChunklen, int recover);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_destroy_inlet(IntPtr obj);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_get_fullinfo(IntPtr obj, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_open_stream(IntPtr obj, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_set_postprocessing(IntPtr obj, ProcessingOptions processingFlags);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_close_stream(IntPtr obj);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_time_correction(IntPtr obj, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_pull_sample_f(IntPtr obj, float[] buffer, int bufferElements, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_pull_sample_d(IntPtr obj, double[] buffer, int bufferElements, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_pull_sample_i(IntPtr obj, int[] buffer, int bufferElements, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_pull_sample_s(IntPtr obj, short[] buffer, int bufferElements, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_pull_sample_c(IntPtr obj, char[] buffer, int bufferElements, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern double lsl_pull_sample_str(IntPtr obj, IntPtr[] buffer, int bufferElements, double timeout, ref int ec);

	//[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	//public static extern double lsl_pull_sample_buf(IntPtr obj, char[][] buffer, uint[] bufferLengths, int bufferElements, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_destroy_string(IntPtr str);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern uint lsl_pull_chunk_f(IntPtr obj, float[,] dataBuffer, double[] timestampBuffer, uint dataBufferElements, uint timestampBufferElements,
											   double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern uint lsl_pull_chunk_d(IntPtr obj,                     double[,] dataBuffer, double[] timestampBuffer, uint dataBufferElements,
											   uint   timestampBufferElements, double    timeout,    ref int  ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern uint lsl_pull_chunk_i(IntPtr obj, int[,] dataBuffer, double[] timestampBuffer, uint dataBufferElements, uint timestampBufferElements,
											   double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern uint lsl_pull_chunk_s(IntPtr obj, short[,] dataBuffer, double[] timestampBuffer, uint dataBufferElements, uint timestampBufferElements,
											   double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern uint lsl_pull_chunk_c(IntPtr obj, char[,] dataBuffer, double[] timestampBuffer, uint dataBufferElements, uint timestampBufferElements,
											   double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern uint lsl_pull_chunk_str(IntPtr obj,                     IntPtr[,] dataBuffer, double[] timestampBuffer, uint dataBufferElements,
												 uint   timestampBufferElements, double    timeout,    ref int  ec);

	//[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	//public static extern uint lsl_pull_chunk_buf(IntPtr obj, char[][,] dataBuffer, uint[,] lengthsBuffer, double[] timestampBuffer, uint dataBufferElements, uint   timestampBufferElements, double timeout, ref int ec);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern uint lsl_samples_available(IntPtr obj);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern uint lsl_was_clock_reset(IntPtr obj);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_first_child(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_last_child(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_next_sibling(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_previous_sibling(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_parent(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_child(IntPtr e, string name);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_next_sibling_n(IntPtr e, string name);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_previous_sibling_n(IntPtr e, string name);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_empty(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_is_text(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_name(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_value(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_child_value(IntPtr e);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_child_value_n(IntPtr e, string name);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_append_child_value(IntPtr e, string name, string value);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_prepend_child_value(IntPtr e, string name, string value);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_set_child_value(IntPtr e, string name, string value);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_set_name(IntPtr e, string rhs);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_set_value(IntPtr e, string rhs);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_append_child(IntPtr e, string name);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_prepend_child(IntPtr e, string name);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_append_copy(IntPtr e, IntPtr e2);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_prepend_copy(IntPtr e, IntPtr e2);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_remove_child_n(IntPtr e, string name);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_remove_child(IntPtr e, IntPtr e2);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_create_continuous_resolver(double forgetAfter);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_create_continuous_resolver_byprop(string prop, string value, double forgetAfter);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern IntPtr lsl_create_continuous_resolver_bypred(string pred, double forgetAfter);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern int lsl_resolver_results(IntPtr obj, IntPtr[] buffer, uint bufferElements);

	[DllImport(LIBNAME, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
	public static extern void lsl_destroy_continuous_resolver(IntPtr obj);
}

#endregion
}
