namespace LSL4Unity.OV.Template
{
/// <summary> Implementation for a Inlet receiving Matrix (double) from OpenViBE. </summary>
/// <seealso cref="OVDoubleInlet" />
/// @todo Je dois d'abord vérifier si je ne peux pas envoyer le tableau d'un coup par LSL et le double ou float Inlet sera suffisant.
public class MatrixInlet : OVDoubleInlet
{
	public int       nChannel = -1;
	public int       nSample  = -1;
	public double[,] matrix;
	public bool      readyToSend = false;

	private int curChannel = -1;
	private int curSample  = -1;

	private void ResetMatrix()
	{
		for (int i = 0; i < nChannel; i++) { for (int j = 0; j < nSample; j++) { matrix[i, j] = 0; } }
		curChannel = 0;
		curSample  = 0;
	}

	protected override void Process(double[] input, double time)
	{
		if (nChannel == -1) { nChannel = (int) input[0]; }
		else if (nSample == -1) {
			nSample = (int) input[0];
			matrix  = new double[nChannel, nSample];
			ResetMatrix();
		}
		else {
			// If We have complete the matrix
			if (curChannel == nChannel && curSample == nSample) { ResetMatrix(); }
			// Update Row and column
			if (curSample == nSample) {
				curChannel++;
				curSample = 0;
			}
			else { curSample++; }

			// If Now the matrix is completed
			if (curChannel == nChannel && curSample == nSample) { readyToSend = true; }
		}
	}
}
}
