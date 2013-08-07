﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sage.Platform.Mashups;
using Sage.Platform.Mashups.Processors;
using Sage.Platform.Mashups.Records;
using UKPSG.Social.Mashups.Localization;
using System.Net;
using System.Xml.Linq;
using System.Web;
using UKPSG.Social.Mashups.Records.Linkedin;
using UKPSG.Social.Mashups.Records;
using System.ComponentModel;

namespace UKPSG.Social.Mashups.Processors
{
    /// <summary>
    /// LinkedIn Mashup
    /// </summary>
    [SRDisplayName("LinkedinProcessor_DisplayName"), System.Xml.Serialization.XmlRoot("queryProcessor")]
    public class LinkedinProcessor : InputProcessorBase
    {
        private const String LINKEDIN_OAUTHPROVIDER_NAME = "LinkedIn";
        private const String URL_NETWORK_UPDATES = "https://api.linkedin.com/v1/people/~/network/updates?scope=self";
        private const String URL_NETWORK_UPDATE_BYMEMBER = "https://api.linkedin.com/v1/people/id={0}/network/updates?scope=self";
        private const String URL_PEOPLE_SEARCH = "https://api.linkedin.com/v1/people-search:(people:(id,first-name,last-name,picture-url,headline,public-profile-url))?keywords={0}&sort=relevance";
        private const String URL_PROFILE_API = "https://api.linkedin.com/v1/people/id={0}:(id,first-name,last-name,picture-url,headline,public-profile-url,formatted-name,location:(name),industry,summary,specialties,positions,educations)";
        private const int LINKEDIN_DEFAULT_MAXRESULTS = 50;

        private String _queryType = "Social";
        [Description("Type of information we want to retrieve: 'Social' (for status updates), 'People' (for user search), 'Profile' (for retrieving a person's profile)")]
        public String QueryType
        {
            get { return this._queryType; }
            set
            {
                this.SetProperty("QueryType", ref this._queryType, value);
            }
        }

        private int? _maximumResults = LINKEDIN_DEFAULT_MAXRESULTS;
        [SRDisplayName("QueryProcessor_MaximumResults_DisplayName"), System.Xml.Serialization.XmlIgnore]
        public int? MaximumResults
        {
            get
            {
                return this._maximumResults;
            }
            set
            {
                this.SetProperty("MaximumResults", ref this._maximumResults, value);
            }
        }

        private String _linkedInUser;
        /// <summary>
        /// User id to display updates from.  Note this is the LinkedIn internal user id (a short alphanumeric string), NOT the user's name or email address
        /// </summary>
        [SRDescription("Linkedin_LinkedInUser_Description")]
        public String LinkedInUser
        {
            get
            {
                return this._linkedInUser;
            }
            set
            {
                this.SetProperty("LinkedInUser", ref this._linkedInUser, value);
            }
        }

        private String _search;
        [Description("Search parameter, for people search (usually specified via a runtime parameter _Search)")]
        public String Search
        {
            get
            {
                return this._search;
            }
            set
            {
                this.SetProperty("Search", ref this._search, value);
            }
        }

        protected override IEnumerable<IRecord> OnProcessRecord(IRecord record, IDictionary<string, object> runtimeParams)
        {
            AuthenticationData auth = AuthenticationData.RetrieveAuthenticationData(LINKEDIN_OAUTHPROVIDER_NAME);
            switch (QueryType)
            {
                case "Social":
                    return ExecuteSocialSearch(auth, runtimeParams);
                case "People":
                    return ExecutePeopleSearch(auth, runtimeParams);
                case "Profile":
                    return ExecuteProfileSearch(auth, runtimeParams);
                default:
                    throw new Exception("Unexpected QueryType value '" + QueryType + "'.  Valid values are Social, People.");
            }
        }

        private IEnumerable<IRecord> ExecuteProfileSearch(AuthenticationData auth, IDictionary<string, object> runtimeParams)
        {
            if (runtimeParams.ContainsKey("LinkedInUser"))
            {
                LinkedInUser = (String)runtimeParams["LinkedInUser"];
            }
            if (String.IsNullOrEmpty(LinkedInUser))
            {
                throw new Exception("This method requires a LinkedInUser parameter");
            }
            String url = String.Format(URL_PROFILE_API, LinkedInUser);
            url = AddAuthentication(url, auth);
            WebClient client = new WebClient();
            String data = Encoding.UTF8.GetString(client.DownloadData(url));
            XDocument xml = XDocument.Parse(data);
            foreach (XElement personNode in xml.Descendants("person"))
            {
                yield return RecordBase.CreateRecord(new LinkedinPerson(personNode));
            }
        }

        private IEnumerable<IRecord> ExecutePeopleSearch(AuthenticationData auth, IDictionary<string, object> runtimeParams)
        {
            WebClient client = new WebClient();
            if (runtimeParams.ContainsKey("Search"))
                Search = (String)runtimeParams["Search"];
            if(String.IsNullOrEmpty(Search))
                throw new Exception("This method requires a Search parameter");
            String url = String.Format(URL_PEOPLE_SEARCH, HttpUtility.UrlEncode(Search));
            url = AddAuthentication(url, auth);
            if (MaximumResults != null)
                url += String.Format("&count={0}", MaximumResults);
            String data = Encoding.UTF8.GetString(client.DownloadData(url));
            XDocument xml = XDocument.Parse(data);
            foreach(XElement personNode in xml.Descendants("person")){
                yield return RecordBase.CreateRecord(new LinkedinPerson(personNode));
            }
        }

        private IEnumerable<IRecord> ExecuteSocialSearch(AuthenticationData auth, IDictionary<string, object> runtimeParams)
        {
            WebClient client = new WebClient();
            String url = URL_NETWORK_UPDATES;
            if (runtimeParams.ContainsKey("LinkedInUser") || !String.IsNullOrEmpty(LinkedInUser))
            {
                object specifiedMemberId;
                if (!runtimeParams.TryGetValue("LinkedInUser", out specifiedMemberId))
                    specifiedMemberId = LinkedInUser;
                url = String.Format(URL_NETWORK_UPDATE_BYMEMBER, specifiedMemberId);
            }
            url = AddAuthentication(url, auth);
            if (MaximumResults != null)
                url += String.Format("&count={0}", MaximumResults);
            String data = Encoding.UTF8.GetString(client.DownloadData(url));
            XDocument xml = XDocument.Parse(data);
            foreach (XElement update in xml.Element("updates").Elements("update"))
            {
                IRecord result = ParseUpdateNode(update);
                if (result != null)
                    yield return result;
            }
        }

        public override IEnumerable<string> PropertyNames
        {
            get
            {
                return base.PropertyNames;
            }
        }

        private IRecord ParseUpdateNode(XElement update)
        {
            StatusUpdateRecord result = null;
            switch (update.Element("update-type").Value)
            {
                case "CONN":  // Connection update
                    result = new LinkedinConnectionUpdate(update);
                    break;
                case "NCON":  // not sure what this one is as opposed to CONN
                    break;
                case "CCEM":  // somebody who is in their address book joined linked in
                    break;
                case "SHAR":  // Status update (share)

                    break;
                case "STAT":  // Status update (simple)
                    result = new LinkedinStatusUpdate(update);
                    break;
                case "VIRL":  // Like / Comment info
                    break;
                case "JGRP":  // Joined a group
                    break;
                case "APPS":
                case "APPM":  // Application Update

                    break;
                case "PICU":  // Profile picture update

                    break;
                default:
                    // something else?
                    break;
            }
            if (result != null)
            {
                result.Icon = "LinkedIn_Logo16px.png";
                return RecordBase.CreateRecord(result);
            }
            return null;
        }

        private string AddAuthentication(string url, AuthenticationData auth)
        {
            if (url.Contains("?"))
                url += "&";
            else
                url += "?";
            url += "oauth2_access_token=" + HttpUtility.UrlEncode(auth.Token);
            return url;
        }
    }
}
