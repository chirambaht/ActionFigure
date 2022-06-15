using System.Collections;
using System.Collections.Generic;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Globalization;

public class player : MonoBehaviour {
	// Start is called before the first frame update
	static int NUMBER_OF_DEVICES = 3;
	static int DATA_POINTS		 = 4;
	static int DATA_START_POINT	 = 3;

	public float rate = 0.1f;

	public TextMeshProUGUI text_stats;
	public TextMeshProUGUI debug_stats;

	public Transform groundCheckTransform;
	public Renderer	 handy_dandy;

	public GameObject bone_shoulder_right;
	public GameObject bone_upper_right;
	public GameObject bone_lower_right;
	public GameObject bone_hand_right;

	Transform shoulder, hand, bicep, forearm;

	float[,] new_values			= new float[NUMBER_OF_DEVICES, DATA_POINTS];
	float[,] base_values		= new float[NUMBER_OF_DEVICES, DATA_POINTS];
	float[,] raw_values			= new float[NUMBER_OF_DEVICES, DATA_POINTS];
	float[,] placed_values		= new float[NUMBER_OF_DEVICES, DATA_POINTS];
	float[,] manipulated_values = new float[NUMBER_OF_DEVICES, DATA_POINTS];

	float horizontalInput;
	float verticalInput;

	float[] offsets = new float[4];
	Rigidbody mainPlayer;

	// Network Variables
	public const int	port  = 9022;
	public const string my_ip = "192.168.1.101";
	Byte[] rec_data			  = new Byte[1024];

	// UDP Variables
	UdpClient client;

	// TCP Variables
	TcpClient	tcp_client;
	TcpListener tcp_listener;
	Thread		networkThread;

	// COrrects the quaternions base on the MPU direction
	public Quaternion quaternion_manipulator( Quaternion incoming_quaternion ) {
		Quaternion temp;
		temp.w = incoming_quaternion.w;
		temp.x = incoming_quaternion.y;
		temp.z = incoming_quaternion.z;
		temp.y = incoming_quaternion.x * -1;

		return temp.normalized;
	}

	public float[] quaternion_to_array( Quaternion incoming_quaternion ) {
		float[] outer = new float[DATA_POINTS];
		outer[0]	  = incoming_quaternion.w;
		outer[1]	  = incoming_quaternion.x;
		outer[2]	  = incoming_quaternion.y;
		outer[3]	  = incoming_quaternion.z;
		return outer;
	}

	public float[,] quaternion_to_array( GameObject a, GameObject b, GameObject c ) {
		float[,] outer = new float[NUMBER_OF_DEVICES, DATA_POINTS];
		float[] data = new float[DATA_POINTS];
		for( int i = 0; i < NUMBER_OF_DEVICES; i++ ) {
			if( i == 0 ) {
				data = quaternion_to_array( a.transform.rotation );
			} else if( i == 1 ) {
				data = quaternion_to_array( b.transform.rotation );
			} else {
				data = quaternion_to_array( c.transform.rotation );
			}
			for( int j = 0; j < DATA_POINTS; j++ ) {
				outer[i, j] = data[j];
			}
		}
		return outer;
	}

	public string float_array_to_string( float[,] incoming_floats ) {
		string temp_string = "";
		for( int outer = 0; outer < NUMBER_OF_DEVICES; outer++ ) {
			for( int i = 0; i < DATA_POINTS; i++ ) {
				temp_string = temp_string + Math.Round( incoming_floats[outer, i], 3 ).ToString() +
					( ( i == DATA_POINTS - 1 ) ? "\n" : "    " );
			}
		}
		return temp_string;
	}

	public void t_pose() {
		// Recenters according to T-Pose
		for( int i = 0; i < NUMBER_OF_DEVICES; i++ ) {
			for( int j = 0; j < DATA_POINTS; j++ ) {
				// base_values[i, j] = ;
				base_values[i, j] = 0 - new_values[i, j];
			}
		}
	}

	void Start() {
		mainPlayer = GetComponent<Rigidbody>();

		bicep	 = bone_upper_right.transform;
		shoulder = bone_shoulder_right.transform;
		hand	 = bone_hand_right.transform;
		forearm	 = bone_lower_right.transform;
		// All game objects to be assigned in the properties of the model.

		client = new UdpClient();
		Array.Clear( new_values, 0, 2 );
		Array.Clear( base_values, 0, 2 );
		Array.Clear( raw_values, 0, 2 );
		Array.Clear( placed_values, 0, 2 );
		Array.Clear( manipulated_values, 0, 2 );

		networkThread			   = new Thread( new ThreadStart( GetNetData ) );
		networkThread.IsBackground = true;
		networkThread.Start();
	}

	/** Returns a wrapped around float between -1 and 1.*/
	public float value_clamper( float incoming_number ) {
		float max = 1f, min = -1f, val = incoming_number;
		if( incoming_number >= max ) {
			float excess = incoming_number % 1;
			val			 = -1 + excess;
		} else if( incoming_number <= min ) {
			float excess = incoming_number % 1;
			val			 = excess;
		}

		return val;
	}

	void GetNetData() {
		// UDP Variables
		// IPEndPoint me = new IPEndPoint(IPAddress.Parse("169.254.121.174"), port);
		// IPEndPoint me = new IPEndPoint(IPAddress.Parse(my_ip), port);
		// client = new UdpClient(me);
		// client.Client.Blocking = false;
		// client.Client.ReceiveTimeout = 100;

		// TCP Variables
		Debug.Log( String.Format( "Started network thread. Listening on: {0}:{1}", my_ip, port ) );

		tcp_listener = new TcpListener( IPAddress.Parse( my_ip ), port );
		tcp_listener.Start();
		int waited_data_messages = 0;
		while( true ) {
			try {
				Debug.Log( "Waiting for a connection... " );
				TcpClient client = tcp_listener.AcceptTcpClient();
				Debug.Log( String.Format( "Connected to client {0}", client.Client.RemoteEndPoint ) );

				NetworkStream stream = client.GetStream();

				while( client.Connected ) {
					if( !stream.DataAvailable ) {
						waited_data_messages++;
						if( waited_data_messages > 10 ) {
							waited_data_messages = 0;
							Debug.Log( "No data received from client." );
							break;
						}
						if( waited_data_messages < 5 ) {
							yield return new WaitForSeconds( 0.01f ); // Wait 10 milliseconds before trying again.
						} else {
							yield return new WaitForSeconds( 0.1f ); // Wait 10 milliseconds before trying again.
						}
						continue;
					}

					int i = stream.Read( rec_data, 0, rec_data.Length );

					// print the received bytes
					Debug.LogFormat( "{0}-{1} {2}-{3} {4}-{5} ...", rec_data[0], rec_data[1], rec_data[2], rec_data[3], rec_data[4], rec_data[5] );

					int[] bytesAsInts = Array.ConvertAll( rec_data, c => ( int ) c );
					// print the received bytes as ints
					Debug.LogFormat( "{0}-{1} {2}-{3} {4}-{5} ...", bytesAsInts[0], bytesAsInts[1], bytesAsInts[2], bytesAsInts[3], bytesAsInts[4], bytesAsInts[5] );
				}
				stream.Dispose();

				// UDP Work
				// byte [] data = new byte[1024];

				// for (var a = 0; a < NUMBER_OF_DEVICES * DATA_POINTS; a++){
				//     data[a] = rec_data[DATA_START_POINT + a];
				// }

				// // encode UTF8-coded bytes to text format
				// string text = Encoding.UTF8.GetString(data);

				// string[] devices = text.Split(':');
				// float t;

				// for (var dev = 0; dev < NUMBER_OF_DEVICES; dev++)
				// {
				//     strin{
				//         t = float.Parse(single_device[val],
				//         System.Globalization.CultureInfo.InvariantCulture);
				//         raw_values[dev, val] = t;
				//         new_values[dev, val] = value_clamper(t);
				//     }
				// }g[] single_device = devices[dev].Split(',');

				//     for (var val = 0; val < DATA_POINTS; val++)
				//     {
				//         t = float.Parse(single_device[val],
				//         System.Globalization.CultureInfo.InvariantCulture);
				//         raw_values[dev, val] = t;
				//         new_values[dev, val] = value_clamper(t);
				//     }
				// }
			} catch( Exception err ) {
				err.ToString();
			} finally {
				client.Close();
			}
		}
	}

	// Update is called once per frame
	void Update() {
		if( Input.GetKeyDown( KeyCode.R ) ) {
			// Print the rotation between forarm and bicep
		}

		if( Input.GetKeyDown( KeyCode.T ) ) {
			t_pose();
		}

		for( int i = 0; i < NUMBER_OF_DEVICES; i++ ) {
			for( int j = 0; j < DATA_POINTS; j++ ) {
				placed_values[i, j] = base_values[i, j] + new_values[i, j];
			}
		}

		bicep.transform.rotation = Quaternion.Lerp(
			bicep.transform.rotation,
			new Quaternion( placed_values[0, 1], placed_values[0, 2], placed_values[0, 3], placed_values[0, 0] ), rate );
		forearm.transform.rotation = Quaternion.Lerp(
			forearm.transform.rotation,
			new Quaternion( placed_values[1, 1], placed_values[1, 2], placed_values[1, 3], placed_values[1, 0] ), rate );
		hand.transform.rotation = Quaternion.Lerp(
			hand.transform.rotation,
			new Quaternion( placed_values[2, 1], placed_values[2, 2], placed_values[2, 3], placed_values[2, 0] ), rate );

		float a1 = Quaternion.Angle( bone_shoulder_right.transform.rotation, bone_lower_right.transform.rotation );
		float a2 = Quaternion.Angle( bone_upper_right.transform.rotation, bone_lower_right.transform.rotation );
		float a3 = Quaternion.Angle( bone_lower_right.transform.rotation, bone_hand_right.transform.rotation );

		manipulated_values = quaternion_to_array( bone_upper_right, bone_lower_right, bone_hand_right );

		text_stats.text =
			String.Format( "Rotations\nBicep - Forearm\t{0}\nForearm - Hand\t{1}\nWrist Rotation\t{2}", a1, a2, a3 );
		debug_stats.text = String.Format(
			"Received:\n{0}\nBases:\n{1}\nTransform:\n{2}\nManipulated:\n{3}\nPlaced:\n{4}",
			float_array_to_string( raw_values ), float_array_to_string( base_values ), float_array_to_string( new_values ),
			float_array_to_string( manipulated_values ), float_array_to_string( placed_values ) );
	}

	void FixedUpdate() {
		if( Physics.OverlapSphere( groundCheckTransform.position, 0.1f ).Length <= 1 ) {
			return;
		}
	}

	private void onTriggerEnter( Collision other ) {
		Debug.Log( "Crash" );
		if( other.gameObject.name == "coin" ) {
			Destroy( other.gameObject );
		}
	}

	void OnApplicationQuit() {
		try {
			client.Close();
		} catch( Exception e ) {
			Debug.Log( e.Message );
		}
	}
}
