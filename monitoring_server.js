#!/usr/bin/env nodejs

const http = require('http');
const fs = require('fs');
//var PORT = 2000; // local conf
var PORT = 334;
const LOGS_DIR = '/var/log/instant-sphere/';

http.createServer((request, response) => {
	var body = [];
	// Collects the data in a array
	request.on('data', (chunk) => {
		body.push(chunk);
	}).on('end', () => {
		// Then concatenates and stringifies it
		body = Buffer.concat(body).toString();
		var data = decodeURIComponent(body.replace(/\+/g, ""));
		data = data.substring(5, data.length); // removes "data="

		saveLogs(data);
		response.end(body);
	}).on('error', (err) => {
	    console.log(err);
	});
}).listen(PORT);

console.log('Server running');

function saveLogs(data) {
	var date = new Date();
	var file = date.toUTCString().replace(/ /g,'_');
	file = file.substring(5, file.length) + '.log';
	fs.writeFile(LOGS_DIR + file, data, function(err) {
		if (err) {
			console.log(err);
		}
		console.log("Saved logs in" + file);
	});
}
