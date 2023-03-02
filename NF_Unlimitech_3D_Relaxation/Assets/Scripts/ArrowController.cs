using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArrowController : MonoBehaviour
{
    #region Variables
    public static float speed;

    private Rigidbody arrowRb;
    #endregion

    // Start is called before the first frame update
    void Awake()
    {
        //Fetch the Rigidbody component you attach from your GameObject
        arrowRb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        //Move the Rigidbody forwards constantly at speed you define
        //arrowRb.velocity = -arrowRb.transform.up * speed;
        arrowRb.transform.position = new Vector3(arrowRb.transform.position.x, arrowRb.transform.position.y, arrowRb.transform.position.z + 0.01f * speed);
    }

    void OnCollisionEnter(Collision collision)
    {
        arrowRb.transform.position = new Vector3(0, 0, 0);
    }
}
