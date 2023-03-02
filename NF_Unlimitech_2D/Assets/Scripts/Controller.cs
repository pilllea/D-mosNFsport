using UnityEngine;
using UnityEngine.UI;
using System;
using System.Linq;
using Assets.LSL4Unity.Scripts.AbstractInlets;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Assets.LSL4Unity.Scripts.Examples
{

    public class Controller : AFloatInlet
    {
        #region Variables
        public GameObject barObject;
        public GameObject particleObject;
        public float threasholdSMR;
        public float threasholdParticles;

        private float lastSample = 0;
        private float mean;
        private float SD;
        private ParticleSystem particleSys;
        private Image barImage;
        #endregion

        private void Awake()
        {
            string[] lines = System.IO.File.ReadAllLines(@"C:\Users\leapi\OneDrive\Bureau\Sport Unlimitech 2021\ScenariiOpenViBE\signals\config.txt");
            mean = float.Parse(lines[1], System.Globalization.CultureInfo.InvariantCulture);
            SD = float.Parse(lines[3], System.Globalization.CultureInfo.InvariantCulture);

            particleSys = particleObject.GetComponent<ParticleSystem>();
            barImage = barObject.GetComponent<Image>();

            Debug.Log(string.Format("Mean : {0} ; SD : {1}", mean.ToString(), SD.ToString()));
        }

        /// <summary>
        /// Is called everytime there is a new LSL stream received
        /// </summary>
        /// <param name="newSample"></param>
        /// <param name="timeStamp"></param>
        protected override void Process(float[] newSample, double timeStamp)
        {
            //Segment the LSLstream
            if (newSample.Length != 0) { 
                lastSample = newSample[0];
            }

            Debug.Log(string.Format("Sample {0}", lastSample.ToString()));

            if (lastSample <= mean) {
                barImage.fillAmount = 0f;
                particleSys.enableEmission = false;
            }
            else if (lastSample > mean + threasholdSMR * SD)
            {
                barImage.fillAmount = 1f;
                particleSys.enableEmission = true;
                particleSys.emissionRate = threasholdParticles;
            }
            else
            {
                particleSys.enableEmission = true;
                barImage.fillAmount = lastSample / (mean + threasholdSMR * SD);
                particleSys.emissionRate = lastSample / (mean + threasholdSMR * SD) * threasholdParticles;
            }
        }
        // Update is called once per frame
        void Update()
        {
            //Used to have the scripts running even when the focus is not on the Unity window
            Application.runInBackground = true;
        }
    }
}
