﻿using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Core.Service;
using UniRx;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Core.Assets
{
	/// <summary>
	/// Utility class to load asset bundles. Can load bundles from web or from the streaming assets folder.
	/// </summary>
	public class AssetBundleLoader
	{
		protected AssetService assetService;
		protected ServiceFramework serviceFramework;
		protected AssetBundleCreateRequest bundleRequest;
		protected Dictionary<string, LoadedBundle> downloadedBundles;
		protected Dictionary<string, UnityEngine.Object> loadedAssets;
		protected Dictionary<string, UnityEngine.Object> editorAssets;

		public AssetBundleLoader(IAssetService service, ServiceFramework app)
		{
			assetService = service as AssetService;
			serviceFramework = app;

			downloadedBundles = new Dictionary<string, LoadedBundle>();
			loadedAssets = new Dictionary<string, UnityEngine.Object>();
		}

		public IObservable<UnityEngine.Object> GetSingleAsset<T>(BundleNeeded bundleNeeded) where T : UnityEngine.Object
		{
			return GetBundle<T>(bundleNeeded);
		}

		public void UnloadAsset(string name, bool unloadAllDependencies)
		{
			name = name.ToLower();

			if (downloadedBundles.ContainsKey(name))
			{
				downloadedBundles[name].Unload(unloadAllDependencies);
				downloadedBundles.Remove(name);
			}
		}

		protected IObservable<UnityEngine.Object> GetBundle<T>(BundleNeeded bundleNeeded) where T : UnityEngine.Object
		{
#if UNITY_EDITOR
			if (EditorPreferences.EDITORPREF_SIMULATE_ASSET_BUNDLES)
			{
				return Observable.FromCoroutine<UnityEngine.Object>((observer, cancellationToken) => SimulateAssetBundle<T>(bundleNeeded, observer, cancellationToken));
			}
#endif
			if (!assetService.UseStreamingAssets)
			{
				return Observable.FromCoroutine<UnityEngine.Object>((observer, cancellationToken) => GetBundleFromWebOrCacheOperation<T>(bundleNeeded, observer, cancellationToken));
			}
			else
			{
				return GetBundleFromStreamingAssets<T>(bundleNeeded);
			}
		}

#if UNITY_EDITOR
		protected IEnumerator SimulateAssetBundle<T>(BundleNeeded bundleNeeded, IObserver<UnityEngine.Object> observer, CancellationToken cancellationToken) where T : UnityEngine.Object
		{
			Debug.Log(("AssetBundleLoader: Simulated | Requesting: " + bundleNeeded.AssetName + " | " + bundleNeeded.BundleName).Colored(Colors.aqua));

			List<T> assets = new List<T>();
			var guids = UnityEditor.AssetDatabase.FindAssets(bundleNeeded.BundleName);

			foreach (var id in guids)
			{
				var path = AssetDatabase.GUIDToAssetPath(id);
				var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
				if (asset && asset.name.ToLower().Equals(bundleNeeded.BundleName.ToLower()))
				{
					assets.Add(asset);
					break;
				}

				yield return null;
			}

			observer.OnNext(assets.First());
			observer.OnCompleted();
		}
#endif

		protected IEnumerator GetBundleFromWebOrCacheOperation<T>(BundleNeeded bundleNeeded, IObserver<UnityEngine.Object> observer, CancellationToken cancellationToken) where T : UnityEngine.Object
		{
			UnityWebRequest www = null;
			ManifestInfo manifestInfo = null;
			AssetBundle bundle;

			Debug.Log(("AssetBundleLoader: " + bundleNeeded.AssetCacheState + " | Requesting: " + bundleNeeded.AssetName + " | " + bundleNeeded.BundleName).Colored(Colors.aqua));

			//look at this again, I might need to keep the crc and hash, at least just the hash from the server..
			//https://github.com/UnityCommunity/UnityLibrary/blob/master/Assets/Scripts/AssetBundles/AssetBundleLoader.cs

			if (bundleNeeded.AssetCacheState.Equals(bundleNeeded.AssetCacheState))
			{
				var manifestPath = GetManifestPath(bundleNeeded);

				www = UnityWebRequest.Get(manifestPath);
				yield return www.SendWebRequest();

				if (www.isNetworkError)
				{
					Debug.LogError("AssetBundleLoader: " + www.error);
					observer.OnError(new System.Exception(www.error));
				}

				manifestInfo = new ManifestInfo(www.downloadHandler.text);
				Debug.Log(("AssetBundleLoader: Aquired Manifest | Hash: " + manifestInfo.Hash + " | Version: " + manifestInfo.Version + " | CRC: " + manifestInfo.CRC).Colored(Colors.aqua));

				www = UnityWebRequest.GetAssetBundle(GetAssetPath(bundleNeeded), manifestInfo.Hash, 0);
			}
			else
			{
				www = UnityWebRequest.GetAssetBundle(GetAssetPath(bundleNeeded));
			}

			yield return www.SendWebRequest();

			bundle = DownloadHandlerAssetBundle.GetContent(www);

			if (www.isNetworkError)
			{
				Debug.LogError("AssetBundleLoader: " + www.error);
				observer.OnError(new System.Exception(www.error));
			}
			else
			{
				ProcessDownloadedBundle<T>(bundleNeeded, new LoadedBundle(bundle, manifestInfo))
					.Subscribe(xs =>
					{
						observer.OnNext(xs);
						observer.OnCompleted();
					});
			}

			www.Dispose();
		}

		protected IObservable<UnityEngine.Object> GetBundleFromStreamingAssets<T>(BundleNeeded bundleNeeded) where T : UnityEngine.Object
		{
			Debug.Log(("AssetBundleLoader: Using StreamingAssets - " + " Requesting:" + bundleNeeded.AssetName + " | " + bundleNeeded.BundleName).Colored(Colors.aqua));
			string path = Path.Combine(Application.streamingAssetsPath, GetAssetPathFromLocalStreamingAssets(bundleNeeded));

			return Observable.FromCoroutine<UnityEngine.Object>((observer, cancellationToken) => RunAssetBundleCreateRequestOperation<T>(AssetBundle.LoadFromFileAsync(path), bundleNeeded, observer, cancellationToken));
		}

		protected IEnumerator RunAssetBundleCreateRequestOperation<T>(UnityEngine.AssetBundleCreateRequest assetBundleCreateRequest, BundleNeeded bundleNeeded, IObserver<UnityEngine.Object> observer, CancellationToken cancellationToken) where T : UnityEngine.Object
		{
			while (!assetBundleCreateRequest.isDone && !cancellationToken.IsCancellationRequested)
				yield return null;

			ProcessDownloadedBundle<T>(bundleNeeded, new LoadedBundle(assetBundleCreateRequest.assetBundle))
				.Subscribe(xs =>
				{
					if (!xs)
						observer.OnError(new System.Exception("RunAssetBundleCreateRequestOperation: Error getting bundle " + bundleNeeded.AssetName));

					observer.OnNext(xs);
					observer.OnCompleted();
				});
		}

		protected string GetAssetPath(BundleNeeded bundleNeeded)
		{
			if (bundleNeeded.AssetCategory.Equals(AssetCategoryRoot.None))
				return assetService.AssetBundlesURL + bundleNeeded.BundleName + "?r=" + (Random.value * 9999999); //this random value prevents caching on the web server
			else
				return assetService.AssetBundlesURL + bundleNeeded.AssetCategory.ToString().ToLower() + "/" + bundleNeeded.BundleName;
		}

		protected string GetManifestPath(BundleNeeded bundleNeeded)
		{
			Debug.Log(("AssetBundleLoader: Loading Manifest " + bundleNeeded.ManifestName).Colored(Colors.aqua));

			if (bundleNeeded.AssetCategory.Equals(AssetCategoryRoot.None))
				return assetService.AssetBundlesURL + bundleNeeded.ManifestName + "?r=" + (Random.value * 9999999); //this random value prevents caching on the web server;
			else
				return assetService.AssetBundlesURL + bundleNeeded.AssetCategory.ToString().ToLower() + "/" + bundleNeeded.ManifestName;
		}

		protected string GetAssetPathFromLocalStreamingAssets(BundleNeeded bundleNeeded)
		{
			if (bundleNeeded.AssetCategory.Equals(AssetCategoryRoot.None))
				return bundleNeeded.BundleName;
			else
				return bundleNeeded.AssetCategory.ToString().ToLower() + "/" + bundleNeeded.BundleName;
		}

		protected string GetAssetPathFromLocalStreamingAssetsManifest(BundleNeeded bundleNeeded)
		{
			if (bundleNeeded.AssetCategory.Equals(AssetCategoryRoot.None))
				return bundleNeeded.ManifestName;
			else
				return bundleNeeded.AssetCategory.ToString().ToLower() + "/" + bundleNeeded.ManifestName;
		}

		protected IObservable<UnityEngine.Object> ProcessDownloadedBundle<T>(BundleNeeded bundleNeeded, LoadedBundle bundle) where T : UnityEngine.Object
		{
			if (!downloadedBundles.ContainsKey(bundleNeeded.BundleName))
				downloadedBundles.Add(bundleNeeded.BundleName, bundle);

			return bundle.LoadAssetAsync<T>(bundleNeeded.AssetName);
		}
	}
}