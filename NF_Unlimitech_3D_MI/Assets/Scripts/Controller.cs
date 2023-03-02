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
        public GameObject arrowObject;
        public float arrowSpeed;
        public float threasholdSMR;
        public float maxDistanceFromCenterTarget;
        public float maxDistanceFromOuterTarget;
        public Button trialButton;
        public Text scoreText;
        public AudioSource soundTrialStart;
        public AudioSource soundTrialEnd;

        private bool ungoingTrial;
        private double currentNbStim;
        private double currentNbStimBelowMean;
        private double currentSum;
        private double currentValue;
        private double finalScore;
        private double lastSample;
        private double mean;
        private double SD;
        private bool goLeft;
        private System.Random alea;
        #endregion

        private void Awake()
        {
            //Retrieves the path to config file
            string basisPath = Directory.GetCurrentDirectory();
            Debug.Log(basisPath);

            //Retrieves the signals from the LSL file
            string[] basisPathSplit = basisPath.Split('\\');
            int indexOrigineFolder = Array.IndexOf(basisPathSplit, "DémosNFsport");
            string[] originPath = new string[indexOrigineFolder + 1];
            Array.Copy(basisPathSplit, originPath, indexOrigineFolder + 1);

            string filesPath = String.Join("\\", originPath) + "\\ScenariiOpenViBE\\signals\\";
            Debug.Log(filesPath);

            //Retrieves the mean and SD variables from the current session
            string[] lines = System.IO.File.ReadAllLines(filesPath + "config.txt");
            mean = float.Parse(lines[1], System.Globalization.CultureInfo.InvariantCulture);
            SD = float.Parse(lines[3], System.Globalization.CultureInfo.InvariantCulture);
            //Display the retrieved mean and SD
            Debug.Log(string.Format("Mean : {0} ; SD : {1}", mean.ToString(), SD.ToString()));

            //Init variables
            alea = new System.Random();
            lastSample = 0;
            ungoingTrial = false;
            scoreText.gameObject.SetActive(true);
            scoreText.enabled = false;
            trialButton.gameObject.SetActive(true);
            trialButton.enabled = true;
        }

        private void Update()
        {
            //Used to have the scripts running even when the focus is not on the Unity window
            Application.runInBackground = true;

            if (arrowObject.transform.position.z >= 4.6)
            {
                Debug.Log("Trial end");

                //Play sound
                soundTrialEnd.Play();

                //Make the trial button and the score text appear
                trialButton.gameObject.SetActive(true);
                trialButton.enabled = true;
                scoreText.enabled = true;

                //Computes the scores and make it appear
                if (currentNbStimBelowMean > 0)
                {
                    finalScore = currentNbStimBelowMean / currentNbStim * 100;
                    scoreText.text = "Score : " + Math.Round(finalScore,2).ToString();
                }
                else
                {
                    scoreText.text = "Score : 0";
                }

                //Re-init arrow and variables for next trial
                ungoingTrial = false;
                arrowObject.transform.position = new Vector3(0, 0, 0);
                ArrowController.speed = 0;
            }
        }

        /// <summary>
        /// Is called when participants clic on the New trial button
        /// </summary>
        public void OnClick()
        {
            Debug.Log("New trial");

            //Play sound
            soundTrialStart.Play();

            //Make the trial button and the score text disappear
            trialButton.gameObject.SetActive(false);
            trialButton.enabled = false;
            scoreText.enabled = false;

            //Set variables for begining of trial
			currentNbStim = 0;
            currentNbStimBelowMean = 0;
            ungoingTrial = true;
            ArrowController.speed = arrowSpeed;

            //Set alea direction for the arrow
            goLeft = (alea.Next(1,10) >= 5) ? true : false;

        }

        /// <summary>
        /// Is called everytime there is a new LSL stream received
        /// </summary>
        /// <param name="newSample"></param>
        /// <param name="timeStamp"></param>
        protected override void Process(float[] newSample, double timeStamp)
        {
            //Segment the LSLstream
            if (newSample.Length != 0)
            {
                //Retrieve last sample
                lastSample = (double)newSample[0];

                //Updates variables for arrow control
                currentNbStim += 1;                 

                //If ungoing trial
                if (ungoingTrial) {
                    if (currentNbStim == 1)
                    {
                        currentSum = lastSample;
                    }
                    else
                    {
                        currentSum += lastSample;
                    }

                    currentValue = (lastSample + currentSum / currentNbStim) / 2 - (alea.Next(0,100) / 100 * SD);
                    //currentValue = lastSample - (alea.Next(0,100) / 100 * SD);

                    //Debug.Log("Current value : " + lastSample.ToString() + ", Alea : " + alea.Next().ToString());
                    //currentValue = currentSum / currentNbStim;
                    //currentValue = (lastSample + currentSum / currentNbStim) / 2;

                    //Update arrow speed if changed in editor
                    ArrowController.speed = arrowSpeed;

                    //If very good performances
                    if (currentValue <= mean - threasholdSMR * SD)
                    {
                        //Debug.Log("Very good - Current average below threashold");
                        Debug.Log(string.Format("Very good sample : {0} < {1} + {2} * {3}", currentValue, mean, threasholdSMR, SD));

                        //Arrow reaching the center of the target 
                        arrowObject.transform.position = new Vector3(0, 0, arrowObject.transform.position.z);
                        //Add one successful epoch to score
                        currentNbStimBelowMean++;
                        //Potentially change the direction of the arrow
                        goLeft = (alea.Next(1, 10) >= 5) ? true : false;
                    }

                    //If good performances
                    else if (currentValue < mean & currentValue > mean - threasholdSMR * SD)
                    {
                        //Debug.Log("Good - Current average below mean");
                        Debug.Log(string.Format("Good sample : {0} < {1}", currentValue, mean));

                        //Arrow reaching the outer part of the target 
                        float newPosition = (float)((currentValue - mean + (threasholdSMR * SD)) / (threasholdSMR * SD) * maxDistanceFromCenterTarget);
                        if (goLeft) {
                            arrowObject.transform.position = new Vector3(-newPosition, arrowObject.transform.position.y, arrowObject.transform.position.z);
                        }
                        else {
                            arrowObject.transform.position = new Vector3(newPosition, arrowObject.transform.position.y, arrowObject.transform.position.z);
                        }
                        //Add one successful epoch to score
                        currentNbStimBelowMean++;
                    }
                    else if (currentValue == mean)
                    {
                        if (goLeft)
                        {
                            arrowObject.transform.position = new Vector3(-maxDistanceFromCenterTarget, arrowObject.transform.position.y, arrowObject.transform.position.z);
                        }
                        else
                        {
                            arrowObject.transform.position = new Vector3(maxDistanceFromCenterTarget, arrowObject.transform.position.y, arrowObject.transform.position.z);
                        }
                    }
                    //If mah performances
                    else if (currentValue > mean & currentValue < mean + threasholdSMR * SD)
                    {
                        //Debug.Log("Mah - Current average above mean");
                        Debug.Log(string.Format("Mah sample : {0} > {1}", currentValue, mean));
                        //Arrow not reaching the target
                        float newPosition = (float)(maxDistanceFromCenterTarget + (currentValue - mean) / (threasholdSMR * SD) * (maxDistanceFromOuterTarget - maxDistanceFromCenterTarget));
                        if (goLeft)
                        {
                            arrowObject.transform.position = new Vector3(-newPosition, arrowObject.transform.position.y, arrowObject.transform.position.z);
                        }
                        else
                        {
                            arrowObject.transform.position = new Vector3(newPosition, arrowObject.transform.position.y, arrowObject.transform.position.z);
                        }
                    }
                    else if (currentValue >= mean + threasholdSMR * SD)
                    {
                        arrowObject.transform.position = new Vector3(maxDistanceFromOuterTarget, arrowObject.transform.position.y, arrowObject.transform.position.z);
                    }
                }
            }
        }
    }
}
