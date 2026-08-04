const TVHclientConfigurationPageVar = {
    pluginUniqueId: '3fd018e5-5e78-4e58-b280-a0c068febee0'
};

function describeConnectionTest(result) {
    const lines = [];

    if (result.HtspSuccess) {
        let htsp = 'HTSP: connected';
        if (result.ServerName || result.ServerVersion) {
            htsp += ' to ' + [result.ServerName, result.ServerVersion].filter(Boolean).join(' ');
        }
        if (result.NegotiatedHtspVersion) {
            htsp += ', protocol version ' + result.NegotiatedHtspVersion;
            if (result.ServerHtspVersion && result.ServerHtspVersion !== result.NegotiatedHtspVersion) {
                htsp += ' (server supports up to ' + result.ServerHtspVersion + ')';
            }
        }
        lines.push(htsp);
    } else {
        lines.push('HTSP: failed - ' + (result.HtspError || 'unknown error'));
    }

    if (result.HttpSuccess) {
        let http = 'HTTP: streaming endpoint reachable';
        if (result.HttpAuthScheme) {
            http += ', ' + result.HttpAuthScheme + ' authentication accepted';
        }
        lines.push(http);

        if (result.ChannelCount === 0) {
            lines.push('Channels: none - TVHeadend offers this user no channels, so Live TV will stay empty. Add and map channels in TVHeadend, and check the user\'s channel rights.');
        } else if (typeof result.ChannelCount === 'number') {
            lines.push('Channels: ' + result.ChannelCount + ' available to this user');
        }
    } else {
        lines.push('HTTP: failed - ' + (result.HttpError || 'unknown error'));
    }

    if (result.WebRoot) {
        lines.push('Web root reported by TVHeadend: ' + result.WebRoot);
    }

    return lines;
}

function renderConnectionTestResult(output, result) {
    output.textContent = '';
    output.classList.remove('hide');

    const status = document.createElement('div');
    status.style.fontWeight = '600';
    status.textContent = result.Success
        ? 'Connection OK'
        : 'Connection incomplete';
    output.appendChild(status);

    const list = document.createElement('ul');
    list.style.margin = '0.4em 0 0 0';
    list.style.paddingLeft = '1.4em';

    describeConnectionTest(result).forEach(function (line) {
        const item = document.createElement('li');
        item.style.marginBottom = '0.2em';
        item.textContent = line;
        list.appendChild(item);
    });

    output.appendChild(list);
}

function renderConnectionTestMessage(output, message) {
    output.textContent = message;
    output.classList.remove('hide');
}

function saveConfiguration(page) {
    return ApiClient.getPluginConfiguration(TVHclientConfigurationPageVar.pluginUniqueId).then(function (config) {
        config.TVH_ServerName = page.querySelector('#txtTVH_ServerName').value;
        config.HTTP_Port = page.querySelector('#txtHTTP_Port').value;
        config.HTSP_Port = page.querySelector('#txtHTSP_Port').value;
        config.Username = page.querySelector('#txtUserName').value;
        config.Password = page.querySelector('#txtPassword').value;
        config.Priority = page.querySelector('#txtPriority').value;
        config.Profile = page.querySelector('#txtProfile').value;
        config.Pre_Padding = page.querySelector('#txtPrePadding').value;
        config.Post_Padding = page.querySelector('#txtPostPadding').value;
        config.ChannelType = page.querySelector('#selChannelType').value;
        config.HideRecordingsChannel = page.querySelector('#chkHideRecordingsChannel').checked;
        config.EnableSubsMaudios = page.querySelector('#chkEnableSubsMaudios').checked;
        config.ForceDeinterlace = page.querySelector('#chkForceDeinterlace').checked;
        return ApiClient.updatePluginConfiguration(TVHclientConfigurationPageVar.pluginUniqueId, config);
    });
}

function testConnection(page) {
    const output = page.querySelector('#connectionTestResult');
    const button = page.querySelector('#btnTestConnection');

    renderConnectionTestMessage(output, 'Saving the settings and testing them...');
    button.disabled = true;

    // The check runs on the server against its own configuration, so the form is saved first -
    // otherwise it would report on the previous settings instead of the ones on screen.
    saveConfiguration(page).then(function (updateResult) {
        Dashboard.processPluginConfigurationUpdateResult(updateResult);
        return ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('TVHeadend/TestConnection'),
            dataType: 'json'
        });
    }).then(function (result) {
        renderConnectionTestResult(output, result);
    }).catch(function (error) {
        renderConnectionTestMessage(output, 'Could not run the test: ' + (error && error.message ? error.message : error));
    }).finally(function () {
        button.disabled = false;
    });
}

export default function (view, params) {
    view.addEventListener('viewshow', function () {
        Dashboard.showLoadingMsg();
        const page = this;
        ApiClient.getPluginConfiguration(TVHclientConfigurationPageVar.pluginUniqueId).then(function(config) {
            page.querySelector('#txtTVH_ServerName').value = config.TVH_ServerName || '';
            page.querySelector('#txtHTTP_Port').value = config.HTTP_Port || '9981';
            page.querySelector('#txtHTSP_Port').value = config.HTSP_Port || '9982';
            page.querySelector('#txtUserName').value = config.Username || '';
            page.querySelector('#txtPassword').value = config.Password || '';
            page.querySelector('#txtPriority').value = config.Priority || '5';
            page.querySelector('#txtProfile').value = config.Profile || '';
            page.querySelector('#txtPrePadding').value = config.Pre_Padding || '0';
            page.querySelector('#txtPostPadding').value = config.Post_Padding || '0';
            page.querySelector('#selChannelType').value = config.ChannelType || 'Ignore';
            page.querySelector('#chkHideRecordingsChannel').checked = config.HideRecordingsChannel || false;
            page.querySelector('#chkEnableSubsMaudios').checked = config.EnableSubsMaudios || false;
            page.querySelector('#chkForceDeinterlace').checked = config.ForceDeinterlace || false;
            Dashboard.hideLoadingMsg();
        });
    });
    view.querySelector('#btnTestConnection').addEventListener('click', function () {
        testConnection(view);
    });
    view.querySelector('.TVHclientConfigurationForm').addEventListener('submit', function (e) {
        e.preventDefault();
        Dashboard.showLoadingMsg();
        saveConfiguration(this).then(Dashboard.processPluginConfigurationUpdateResult);
        return false;
    });
}
