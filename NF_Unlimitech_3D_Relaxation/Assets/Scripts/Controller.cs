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
        public LayerMask groundLayer;
        public float threasholdSMR;
        public Button trialButton;
        public Text scoreText;
        public float trialTime;
        public GameObject prefabObject;
        public GameObject mushroomsFolder;
        public int sizeCircle;
        public int maxMush;
        public int mushCount;
        public GameObject cloudObject;
        public float threasholdClouds;
        public AudioSource trialStartSound;
        public AudioSource trialEndSound;

        private bool ungoingTrial;
        private double currentTrialTime;
        private double currentSum;
        private double currentNbStim;
        private double currentNbStimAboveMean;
        private double finalScore;
        private double lastSample;
        private double currentValue;
        private double mean;
        private double SD;
        private List<GameObject> generatedMush;
        private ParticleSystem particleSys;
        private System.Random alea;

        //Variables for random positions generation
        private float newX;
        private float newY;
        private float newZ;
        #endregion

        private void Awake()
        {
            //Retrieves the path to config file
            string basisPath = Directory.GetCurrentDirectory();
            Debug.Log(basisPath);

            //Retrieves the signals from the LSL file
            string[] basisPathSplit = basisPath.Split('\\');
            int indexOrigineFolder = Array.IndexOf(basisPathSplit, "DemosNFsport");
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
            lastSample = 0;
            ungoingTrial = false;
            generatedMush = new List<GameObject>();
            particleSys = cloudObject.GetComponent<ParticleSystem>();
            alea = new System.Random();

            //Add or remove clouds, score text and trial button 
            cloudObject.SetActive(false);
            scoreText.gameObject.SetActive(true);
            scoreText.enabled = false;
            trialButton.gameObject.SetActive(true);
            trialButton.enabled = true;
        }

        private void Update()
        {
            //Used to have the scripts running even when the focus is not on the Unity window
            Application.runInBackground = true;

            //Updates the timing
            currentTrialTime -= Time.deltaTime;
            if (currentTrialTime < 0 && ungoingTrial)
            {
                Debug.Log("Trial end");

                //Play the sound
                trialEndSound.Play();

                //Make the trial button and the score text appear
                trialButton.gameObject.SetActive(true);
                trialButton.enabled = true;
                scoreText.enabled = true;

                //Computes the scores and make it appear
                if (currentNbStimAboveMean > 0)
                {
                    finalScore = currentNbStimAboveMean / currentNbStim * 100;
                    scoreText.text = "Score : " + Math.Round(finalScore,2).ToString();
                }
                else
                {
                    scoreText.text = "Score : 0";
                }

                //Re-init clouds and variables for next trial
                ungoingTrial = false;
                cloudObject.SetActive(false);
                particleSys.Stop();
            }
        }

        public void OnClick()
        {
            Debug.Log("New trial");

            //Play the sound
            trialStartSound.Play();

            //Make the trial button and the score text disappear
            trialButton.gameObject.SetActive(false);
            trialButton.enabled = false;
            scoreText.enabled = false;

            //Set variables for begining of trial
            currentNbStim = 0;
            currentNbStimAboveMean = 0;
            currentTrialTime = trialTime;
            ungoingTrial = true;
            cloudObject.SetActive(true);
            particleSys.Play();
        }

        public float DetectGroundHeight(Vector3 position)
        {
            position.y = 100;
            RaycastHit hit = new RaycastHit();
            Ray ray = new Ray(position, Vector3.down);
            if (Physics.Raycast(ray, out hit, 1000, groundLayer))
            {
                return hit.point.y;
            }
            return 0;
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
                //Displays the mush count
                mushCount = generatedMush.Count;

                //Updates variables for mush and cloud control
                currentNbStim += 1;

                //If ungoing trial
                if (ungoingTrial) {
                    if (currentNbStim == 1) { 
                        currentSum = lastSample;
                    }
                    else { 
                        currentSum += lastSample;
                    }

                    currentValue = currentSum / currentNbStim;
                    //currentValue = (lastSample + currentSum / currentNbStim) / 2 + (alea.Next(0, 100) / 100 * SD);
                    //currentValue = (lastSample + currentSum / currentNbStim) / 2;

                    //If very good performances
                    if (currentValue >= mean + threasholdSMR * SD)
                    {
                        Debug.Log("Very good - Current average above threashold");
                        currentNbStimAboveMean++;

                        //Destroys all the mush
                        int count = generatedMush.Count;
                        while (count > 0)
                        {
                            Destroy(generatedMush.ElementAt(count - 1));
                            count--;
                        }
                        generatedMush.Clear();

                        //Removes all the clouds
                        particleSys.enableEmission = false;
                    }

                    //If good performances
                    else if (currentValue > mean & currentValue < mean + threasholdSMR * SD)
                    {
                        Debug.Log("Good - Current average above mean");
                        currentNbStimAboveMean++;

                        int adequateNbrMush = (int)((currentValue - mean) / (threasholdSMR * SD) * maxMush);
                        if (adequateNbrMush > maxMush & adequateNbrMush < 0)
                        {
                            Debug.Log("Problem with current value : " + currentValue.ToString() + ", lastSample : " + lastSample.ToString() + ", currentAvg : " + (currentSum / currentNbStim).ToString());
                        }
                        else
                        {
                            //If more mush needed to reach adequate count
                            if (generatedMush.Count < adequateNbrMush)
                            {
                                while (generatedMush.Count < adequateNbrMush)
                                {
                                    Vector2 newPosition = UnityEngine.Random.insideUnitCircle * sizeCircle;
                                    newX = newPosition.x - 0.286f;
                                    newZ = newPosition.y + 5.506f;
                                    newY = DetectGroundHeight(new Vector3(newX, 0, newZ));
                                    generatedMush.Add(Instantiate(prefabObject, new Vector3(newX, newY, newZ), Quaternion.identity, mushroomsFolder.transform));
                                }

                                //while (generatedMush.Count < (int)(maxMush - ((currentValue - mean) / (threasholdSMR * SD) * maxMush)))
                                //{
                                //    randX = (float)alea.NextDouble();
                                //    randZ = (float)alea.NextDouble();
                                //    newX = (randX * (26 + 33)) - 33;
                                //    newZ = randZ * 36;
                                //    newY = DetectGroundHeight(new Vector3(newX, 0, newZ));
                                //    generatedMush.Add(Instantiate(prefabObject, new Vector3(newX, newY, newZ), Quaternion.identity, mushroomsFolder.transform));
                                //}

                            }

                            //If less mush needed to reach adequate count
                            else
                            {
                                int count = generatedMush.Count;
                                while (count > adequateNbrMush & count > 0)
                                {
                                    Destroy(generatedMush.ElementAt(count - 1));
                                    generatedMush.RemoveAt(count - 1);
                                    count--;
                                }
                            }
                            particleSys.enableEmission = true;
                            var emission = particleSys.emission;
                            emission.rateOverTime = (float)(((currentValue - mean) / (threasholdSMR * SD) * threasholdClouds));
                            //emission.rateOverTime = ((mean + threasholdSMR * SD) - currentValue) / (mean + threasholdSMR * SD) * threasholdClouds;
                        }
                    }

                    //If mah performances
                    else
                    {
                        Debug.Log("Mah - Current average below mean");
                        //Generates as much mush as needed to reach maxMush
                        int count = generatedMush.Count;
                        while (count < maxMush)
                        {
                            Vector2 newPosition = UnityEngine.Random.insideUnitCircle * sizeCircle;
                            newX = newPosition.x - 0.286f;
                            newZ = newPosition.y + 5.506f;
                            newY = DetectGroundHeight(new Vector3(newX, 0, newZ));
                            generatedMush.Add(Instantiate(prefabObject, new Vector3(newX, newY, newZ), Quaternion.identity, mushroomsFolder.transform));

                            count++;

                            //randX = (float)alea.NextDouble();
                            //randZ = (float)alea.NextDouble();
                            //newX = (randX * (26 + 33)) - 33;
                            //newZ = randZ * 36;
                            //newY = DetectGroundHeight(new Vector3(newX, 0, newZ));
                            //generatedMush.Add(Instantiate(prefabObject, new Vector3(newX, newY, newZ), Quaternion.identity, mushroomsFolder.transform));
                        }
                        particleSys.enableEmission = true;
                        var emission = particleSys.emission;
                        emission.rateOverTime = threasholdClouds;
                    }
                }
            }
        }
    }
}