using FullSerializer;

using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SocialPlatforms;

namespace Rto.Library
{
	public class PlayGamesController : MonoBehaviour
	{
		public static PlayGamesController Instance;

		[HideInInspector]
		public ProfileInfo profileInfo;

		public event EventHandler OnSignedSucces;
		public event EventHandler OnSignedFailed;

		private int cachedScores = 0;

		private void Awake()
		{
			if (Instance == null)
			{
				Instance = this;
				SignIn(SignInInteractivity.CanPromptOnce);
				DontDestroyOnLoad(this);
			}
			else
			{
				Destroy(Instance);
			}

		}

		public void OnSigInPressed()
		{
			PlayGamesPlatform.Activate();
			PlayGamesPlatform.Instance.ManuallyAuthenticate(ProcessAuthentication);
		}

		public void SignIn(SignInInteractivity interactivity)
		{
			PlayGamesPlatform.Activate();
			PlayGamesPlatform.Instance.Authenticate(ProcessAuthentication);
		}

		private void ProcessAuthentication(SignInStatus signInStatus)
		{
			if (signInStatus == SignInStatus.Success)
			{
				profileInfo.ProfileName = PlayGamesPlatform.Instance.GetUserDisplayName();
				profileInfo.codeId = PlayGamesPlatform.Instance.GetUserId();
				profileInfo.imgUrl = PlayGamesPlatform.Instance.GetUserImageUrl();

				OnSignedSucces?.Invoke(this, EventArgs.Empty);
			}
			else
			{
				OnSignedFailed?.Invoke(this, EventArgs.Empty);
			}
		}

		#region Load & Save Score
		public void FetchScoreFromServer(Action onLoadedScore)
		{
			PlayGamesPlatform.Instance.LoadScores("GPGSIds.leaderboard_leaderboard", LeaderboardStart.TopScores, 10,
				LeaderboardCollection.Public,
				LeaderboardTimeSpan.AllTime,
				(LeaderboardScoreData data) =>
				{
					List<string> userIDs = new List<string>();

					Dictionary<string, IScore> userScores = new Dictionary<string, IScore>();
					for (int i = 0; i < data.Scores.Length; i++)
					{
						IScore score = data.Scores[i];
						userIDs.Add(score.userID);
						userScores[score.userID] = score;
					}

					Dictionary<string, string> userNames = new Dictionary<string, string>();
					Social.LoadUsers(userIDs.ToArray(), (users) =>
					{
						for (int i = 0; i < users.Length; i++)
						{
							userNames[users[i].id] = users[i].userName;
						}
						for (int i = 0; i < data.Scores.Length; i++)
						{
							IScore score = data.Scores[i];

							string userName = userNames[score.userID];
							if (userName == Social.localUser.userName)
							{
								cachedScores = Mathf.RoundToInt(score.value);
							}
						}
						onLoadedScore?.Invoke();
					});
				});
		}

		public void ReportScore(int score)
		{
			cachedScores += score;

			Social.ReportScore(cachedScores,
				"GPGSIds.leaderboard_leaderboard",
				(bool success) =>
				{
					string msg = success ? "Score successfully reported to leaderboard." : "Failed to report score to leaderboard.";

					Debug.Log(msg);
				});
		}
		#endregion

		#region Load, Delete, Save Game State
		public void OpenSavedGame<T>(string fileName, Action<T> data) where T : class
		{
			ISavedGameClient savedGameClient = PlayGamesPlatform.Instance.SavedGame;

			savedGameClient.OpenWithAutomaticConflictResolution(
				fileName, // File Name
				DataSource.ReadCacheOrNetwork,
				ConflictResolutionStrategy.UseLongestPlaytime,
				(SavedGameRequestStatus status, ISavedGameMetadata game) =>
				{
					if (status == SavedGameRequestStatus.Success)
					{
						LoadGameState(game, data);
					}
				});
		}

		private void LoadGameState<T>(ISavedGameMetadata game, Action<T> dataAction) where T : class
		{
			ISavedGameClient savedGameClient = PlayGamesPlatform.Instance.SavedGame;
			savedGameClient.ReadBinaryData(game, (SavedGameRequestStatus status, byte[] data) =>
			{
				if (status == SavedGameRequestStatus.Success)
				{
					object deserialize = null;
					string text = ASCIIEncoding.ASCII.GetString(data);

					if (string.IsNullOrEmpty(text))
					{
						Debug.LogError("Data is null or empty.");
						dataAction?.Invoke(null);
						return;
					}

					try
					{
						var newData = fsJsonParser.Parse(text);
						fsSerializer fs = new fsSerializer();
						fs.TryDeserialize(newData, typeof(T), ref deserialize).AssertSuccessWithoutWarnings();

					}
					catch (Exception e)
					{
						Debug.LogError($"Deserialization failed: {e.Message}");
					}

					dataAction?.Invoke(deserialize as T);
				}
				else
				{
					Debug.LogError("Failed To Load");
				}

			});
		}

		public void DeleteSaveGame(string fileName)
		{
			ISavedGameClient savedGameClient = PlayGamesPlatform.Instance.SavedGame;

			savedGameClient.OpenWithAutomaticConflictResolution(
				fileName, // File Name
				DataSource.ReadCacheOrNetwork,
				ConflictResolutionStrategy.UseLongestPlaytime,
				(SavedGameRequestStatus status, ISavedGameMetadata game) =>
				{
					if (status == SavedGameRequestStatus.Success)
					{
						ISavedGameClient savedGameClient = PlayGamesPlatform.Instance.SavedGame;
						savedGameClient.Delete(game);
					}
				});
		}

		public void SaveGame<T>(string fileName, T data, Action onSaveComplete = null)
		{
			fsData serializedData;
			var serializer = new fsSerializer();
			serializer.TrySerialize(data, out serializedData).AssertSuccessWithoutWarnings();
			string json = fsJsonPrinter.PrettyJson(serializedData);

			ISavedGameClient savedGameClient = PlayGamesPlatform.Instance.SavedGame;
			savedGameClient.OpenWithAutomaticConflictResolution(fileName,
				DataSource.ReadCacheOrNetwork,
				ConflictResolutionStrategy.UseLongestPlaytime,
				(SavedGameRequestStatus status, ISavedGameMetadata metadata) =>
				{
					SavedGameMetadataUpdate updateForMetadata = new SavedGameMetadataUpdate.Builder().WithUpdatedDescription("I have update my game at: " + DateTime.Now.ToString()).Build();

					byte[] mydata = ASCIIEncoding.ASCII.GetBytes(json);
					((PlayGamesPlatform)Social.Active).SavedGame.CommitUpdate(
						metadata, 
						updateForMetadata, 
						mydata,
						(SavedGameRequestStatus status, ISavedGameMetadata metadata) =>
						{
							if (status == SavedGameRequestStatus.Success)
							{
								Debug.Log("Successfully saved To Cloud");
							}
							else
							{
								Debug.Log("Failed to save to Cloud");
							}

							onSaveComplete?.Invoke();
						});
				});
		}
		#endregion

		#region TimeManager
		public static DateTime GlobalTime;
		[SerializeField]
		private DateTime dateTime;

		public IEnumerator FetchGlobalTime(Action onFetchComplete)
		{
			string url = "https://timeapi.io/api/time/current/zone?timeZone=Asia%2FJakarta";
			using (UnityWebRequest request = UnityWebRequest.Get(url))
			{
				yield return request.SendWebRequest();

				if (request.result == UnityWebRequest.Result.Success)
				{
					string json = request.downloadHandler.text;
					GlobalTime = ParseTimeFromJson(json);
				}
				else
				{
					GlobalTime = DateTime.Now;
					Debug.LogError("Failed to fetch global time.");
				}
			}

			onFetchComplete?.Invoke();
		}

		private DateTime ParseTimeFromJson(string json)
		{
			object deserialize = null;
			var newData = fsJsonParser.Parse(json);
			fsSerializer fs = new fsSerializer();
			fs.TryDeserialize(newData, typeof(TimeResponse), ref deserialize).AssertSuccessWithoutWarnings();
			TimeResponse response = deserialize as TimeResponse;

			return DateTime.Parse(response.dateTime);
		}
		#endregion
	}

	[System.Serializable]
	public class ProfileInfo
	{
		public string ProfileName;
		public string codeId;
		public string imgUrl;
	}
}
