/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Remoting;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Runspaces.Internal;
using System.Management.Automation.Host;
using System.Threading;
using Dbg = System.Management.Automation.Diagnostics;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// This cmdlet is used to retrieve runspaces from the global cache
    /// and write it to the pipeline. The runspaces are wrapped and
    /// returned as PSSession objects.
    /// 
    /// The cmdlet can be used in the following ways:
    /// 
    /// List all the available runspaces
    ///     get-pssession
    /// 
    /// Get the PSSession from session name
    ///     get-pssession -Name sessionName
    /// 
    /// Get the PSSession for the specified ID
    ///     get-pssession -Id sessionId
    ///     
    /// Get the PSSession for the specified instance Guid
    ///     get-pssession -InstanceId sessionGuid
    ///     
    /// Get PSSessions from remote computer.  Optionally filter on state, session instanceid or session name.
    ///     get-psession -ComputerName computerName -StateFilter Disconnected
    /// 
    /// Get PSSessions from virtual machine. Optionally filter on state, session instanceid or session name.
    ///     get-psession -VMName vmName -Name sessionName
    ///     
    /// Get PSSessions from container. Optionally filter on state, session instanceid or session name.
    ///     get-psession -ContainerId containerId -InstanceId instanceId
    ///     
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "PSSession", DefaultParameterSetName = PSRunspaceCmdlet.NameParameterSet,
        HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135219", RemotingCapability = RemotingCapability.OwnedByCommand)]
    [OutputType(typeof(PSSession))]
    public class GetPSSessionCommand : PSRunspaceCmdlet, IDisposable
    {
        #region Parameters

        private const string ConnectionUriParameterSet = "ConnectionUri";
        private const string ConnectionUriInstanceIdParameterSet = "ConnectionUriInstanceId";

        /// <summary>
        /// Computer names to connect to.
        /// </summary>
        [Parameter(Position = 0,
                   Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(Position = 0,
                   Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [ValidateNotNullOrEmpty]
        [Alias("Cn")]
        public override String[] ComputerName
        {
            get { return this.computerNames; }
            set { this.computerNames = value; }
        }
        private String[] computerNames;

        /// <summary>
        /// This parameters specifies the appname which identifies the connection
        /// end point on the remote machine. If this parameter is not specified
        /// then the value specified in DEFAULTREMOTEAPPNAME will be used. If thats
        /// not specified as well, then "WSMAN" will be used
        /// </summary>
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        public String ApplicationName
        {
            get { return appName; }
            set
            {
                appName = ResolveAppName(value);
            }
        }
        private String appName;

        /// <summary>
        /// A complete URI(s) specified for the remote computer and shell to 
        /// connect to and create a runspace for.
        /// </summary>
        [Parameter(Position = 0, Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(Position = 0, Mandatory = true,
                   ValueFromPipelineByPropertyName = true,
                   ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        [ValidateNotNullOrEmpty]
        [Alias("URI", "CU")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public Uri[] ConnectionUri
        {
            get { return uris; }
            set { uris = value; }
        }
        private Uri[] uris;

        /// <summary>
        /// If this parameter is not specified then the value specified in
        /// the environment variable DEFAULTREMOTESHELLNAME will be used. If 
        /// this is not set as well, then Microsoft.PowerShell is used.
        /// </summary>
        [Parameter(ValueFromPipelineByPropertyName = true,
                           ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                           ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                           ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                           ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        public String ConfigurationName
        {
            get { return shell; }
            set 
            {                 
                shell = ResolveShell(value);
            }
        }
        private String shell;

        /// <summary>
        /// The AllowRediraction parameter enables the implicit redirection functionality.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        public SwitchParameter AllowRedirection
        {
            get { return this.allowRedirection; }
            set { this.allowRedirection = value; }
        }
        private bool allowRedirection = false;

        /// <summary>
        /// Session names to filter on.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.NameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ContainerIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ContainerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.VMIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.VMNameParameterSet)]
        [ValidateNotNullOrEmpty()]
        public override string[] Name
        {
            get { return base.Name; }
            set { base.Name = value; }
        }

        /// <summary>
        /// Instance Ids to filter on.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet,
                   Mandatory = true)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet,
                   Mandatory = true)]
        [Parameter(ValueFromPipelineByPropertyName = true,
                   ParameterSetName = PSRunspaceCmdlet.InstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ContainerIdInstanceIdParameterSet,
                   Mandatory = true)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ContainerNameInstanceIdParameterSet,
                   Mandatory = true)]
        [Parameter(ParameterSetName = GetPSSessionCommand.VMIdInstanceIdParameterSet,
                   Mandatory = true)]
        [Parameter(ParameterSetName = GetPSSessionCommand.VMNameInstanceIdParameterSet,
                   Mandatory = true)]
        [ValidateNotNull]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public override Guid[] InstanceId
        {
            get { return base.InstanceId; }
            set { base.InstanceId = value; }
        }

        /// <summary>
        /// Specifies the credentials of the user to impersonate in the 
        /// remote machine. If this parameter is not specified then the 
        /// credentials of the current user process will be assumed.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        [Credential()]
        public PSCredential Credential
        {
            get { return this.psCredential; }
            set
            {
                this.psCredential = value;

                PSRemotingBaseCmdlet.ValidateSpecifiedAuthentication(Credential, CertificateThumbprint, Authentication);
            }
        }
        private PSCredential psCredential;


        /// <summary>
        /// Use basic authentication to authenticate the user.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        public AuthenticationMechanism Authentication
        {
            get { return this.authentication; }
            set
            {
                this.authentication = value;

                PSRemotingBaseCmdlet.ValidateSpecifiedAuthentication(Credential, CertificateThumbprint, Authentication);
            }
        }
        private AuthenticationMechanism authentication;


        /// <summary>
        /// Specifies the certificate thumbprint to be used to impersonate the user on the 
        /// remote machine.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        public string CertificateThumbprint
        {
            get { return this.thumbprint; }
            set
            {
                this.thumbprint = value;

                PSRemotingBaseCmdlet.ValidateSpecifiedAuthentication(Credential, CertificateThumbprint, Authentication);
            }
        }
        private string thumbprint;


        /// <summary>
        /// Port specifies the alternate port to be used in case the 
        /// default ports are not used for the transport mechanism
        /// (port 80 for http and port 443 for useSSL)
        /// </summary>
        /// <remarks>
        /// Currently this is being accepted as a parameter. But in future
        /// support will be added to make this a part of a policy setting.
        /// When a policy setting is in place this parameter can be used
        /// to override the policy setting
        /// </remarks>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [ValidateRange((Int32)1, (Int32)UInt16.MaxValue)]
        public Int32 Port
        {
            get
            {
                return port;
            }
            set
            {
                port = value;
            }
        }
        private Int32 port;


        /// <summary>
        /// This parameter suggests that the transport scheme to be used for
        /// remote connections is useSSL instead of the default http.Since
        /// there are only two possible transport schemes that are possible
        /// at this point, a SwitchParameter is being used to switch between
        /// the two.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "SSL")]
        public SwitchParameter UseSSL
        {
            get
            {
                return useSSL;
            }
            set
            {
                useSSL = value;
            }
        }
        private SwitchParameter useSSL;

        /// <summary>
        /// Allows the user of the cmdlet to specify a throttling value
        /// for throttling the number of remote operations that can
        /// be executed simultaneously.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        public Int32 ThrottleLimit
        {
            get { return this.throttleLimit; }
            set { this.throttleLimit = value; }
        }
        private Int32 throttleLimit = 0;


        /// <summary>
        /// Filters returned remote runspaces based on runspace state.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ContainerIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ContainerIdInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ContainerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ContainerNameInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.VMIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.VMIdInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.VMNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.VMNameInstanceIdParameterSet)]
        public SessionFilterState State
        {
            get { return this.filterState; }
            set { this.filterState = value; }
        }
        private SessionFilterState filterState;


        /// <summary>
        /// Session options.
        /// </summary>
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerNameParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ComputerInstanceIdParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriParameterSet)]
        [Parameter(ParameterSetName = GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)]
        public PSSessionOption SessionOption
        {
            get { return this.sessionOption; }
            set { this.sessionOption = value; }
        }
        private PSSessionOption sessionOption;

        #endregion

        #region Overrides

        /// <summary>
        /// Get the list of runspaces from the global cache and write them
        /// down. If no computername or instance id is specified then
        /// list all runspaces
        /// </summary>
        protected override void ProcessRecord()
        {
            if ((ParameterSetName == GetPSSessionCommand.NameParameterSet) && ((Name == null) || (Name.Length == 0)))
            {
                // that means Get-PSSession (with no parameters)..so retrieve all the runspaces.
                GetAllRunspaces(true, true);
            }
            else if (ParameterSetName == GetPSSessionCommand.ComputerNameParameterSet ||
                     ParameterSetName == GetPSSessionCommand.ComputerInstanceIdParameterSet ||
                     ParameterSetName == GetPSSessionCommand.ConnectionUriParameterSet ||
                     ParameterSetName == GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)
            {
                // Perform the remote query for each provided computer name.
                QueryForRemoteSessions();
            }
            else
            {
                GetMatchingRunspaces(true, true, this.State);
            }
        } // ProcessRecord

        /// <summary>
        /// End processing clean up.
        /// </summary>
        protected override void EndProcessing()
        {
            this.stream.ObjectWriter.Close();
        }

        /// <summary>
        /// User has signaled a stop for this cmdlet.
        /// </summary>
        protected override void StopProcessing()
        {
            this.queryRunspaces.StopAllOperations();
        }

        #endregion Overrides

        #region Private Methods

        /// <summary>
        /// Creates a connectionInfo object for each computer name and performs a remote
        /// session query for each computer filtered by the filterState parameter.
        /// </summary>
        private void QueryForRemoteSessions()
        {
            // Get collection of connection objects for each computer name or 
            // connection uri.
            Collection<WSManConnectionInfo> connectionInfos = GetConnectionObjects();

            // Query for sessions.
            Collection<PSSession> results = this.queryRunspaces.GetDisconnectedSessions(connectionInfos, this.Host, this.stream,
                                                                                        this.RunspaceRepository, this.throttleLimit, 
                                                                                        this.filterState, InstanceId, Name, ConfigurationName);

            // Write any error output from stream object.
            Collection<object> streamObjects = this.stream.ObjectReader.NonBlockingRead();
            foreach (object streamObject in streamObjects)
            {
                if (this.IsStopping)
                {
                    break;
                }

                WriteStreamObject((Action<Cmdlet>)streamObject);
            }

            // Write each session object.
            foreach (PSSession session in results)
            {
                if (this.IsStopping)
                {
                    break;
                }

                WriteObject(session);
            }
        }

        private Collection<WSManConnectionInfo> GetConnectionObjects()
        {
            Collection<WSManConnectionInfo> connectionInfos = new Collection<WSManConnectionInfo>();

            if (ParameterSetName == GetPSSessionCommand.ComputerNameParameterSet ||
                ParameterSetName == GetPSSessionCommand.ComputerInstanceIdParameterSet)
            {
                string scheme = UseSSL.IsPresent ? WSManConnectionInfo.HttpsScheme : WSManConnectionInfo.HttpScheme;

                foreach (string computerName in ComputerName)
                {
                    WSManConnectionInfo connectionInfo = new WSManConnectionInfo();
                    connectionInfo.Scheme = scheme;
                    connectionInfo.ComputerName = ResolveComputerName(computerName);
                    connectionInfo.AppName = ApplicationName;
                    connectionInfo.ShellUri = ConfigurationName;
                    connectionInfo.Port = Port;
                    if (CertificateThumbprint != null)
                    {
                        connectionInfo.CertificateThumbprint = CertificateThumbprint;
                    }
                    else
                    {
                        connectionInfo.Credential = Credential;
                    }
                    connectionInfo.AuthenticationMechanism = Authentication;
                    UpdateConnectionInfo(connectionInfo);

                    connectionInfos.Add(connectionInfo);
                }
            }
            else if (ParameterSetName == GetPSSessionCommand.ConnectionUriParameterSet ||
                     ParameterSetName == GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)
            {
                foreach (var connectionUri in ConnectionUri)
                {
                    WSManConnectionInfo connectionInfo = new WSManConnectionInfo();
                    connectionInfo.ConnectionUri = connectionUri;
                    connectionInfo.ShellUri = ConfigurationName;
                    if (CertificateThumbprint != null)
                    {
                        connectionInfo.CertificateThumbprint = CertificateThumbprint;
                    }
                    else
                    {
                        connectionInfo.Credential = Credential;
                    }
                    connectionInfo.AuthenticationMechanism = Authentication;
                    UpdateConnectionInfo(connectionInfo);

                    connectionInfos.Add(connectionInfo);
                }
            }

            return connectionInfos;
        }

        /// <summary>
        /// Updates connection info with the data read from cmdlet's parameters.
        /// </summary>
        /// <param name="connectionInfo"></param>
        private void UpdateConnectionInfo(WSManConnectionInfo connectionInfo)
        {
            if (ParameterSetName != GetPSSessionCommand.ConnectionUriParameterSet &&
                ParameterSetName != GetPSSessionCommand.ConnectionUriInstanceIdParameterSet)
            {
                // uri redirection is supported only with URI parmeter set
                connectionInfo.MaximumConnectionRedirectionCount = 0;
            }

            if (!this.allowRedirection)
            {
                // uri redirection required explicit user consent
                connectionInfo.MaximumConnectionRedirectionCount = 0;
            }

            // Update the connectionInfo object with passed in session options.
            if (SessionOption != null)
            {
                connectionInfo.SetSessionOptions(SessionOption);
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Dispose method of IDisposable.
        /// </summary>
        public void Dispose()
        {
            this.stream.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private Members

        // Object used for querying remote runspaces.
        QueryRunspaces queryRunspaces = new QueryRunspaces();

        // Object to collect output data from multiple threads.
        private ObjectStream stream = new ObjectStream();

        #endregion

    }
}